using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using SpineTools;
/*
Copyright 2013 Health and Social Care Information Centre,
 *  Solution Assurance <damian.murphy@nhs.net>

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
 */
namespace SpineTools.Connection
{
    /**
     * Class to handle listening for connections inbound from TMS, establishing a mutually-authenticated
     * connection, and for ebXML de-duplication, acknowledgments, construction of a representation of a
     * received message, and handing it off for processing.
     */ 
    internal class Listener
    {
        private const int DEFAULT_PORT = 443;
        private const string DEFAULT_ADDRESS = "0.0.0.0";
        private const string LOGSOURCE = "Spine connection listener";
        private const string EBXMLACKTEMPLATE = "SpineTools.Connection.ebxmlacktemplate.txt";

        private const string EMPTY_RESPONSE = "HTTP/1.1 202 OK\r\nContent-Length: 0\r\nConnection: close\r\nContent-Type: text/xml\r\n\r\n";
        private const string ACK_HTTP_HEADER = "HTTP/1.1 202 OK\r\nContent-Length: __CONTENT_LENGTH__\r\nConnection: close\r\nContent-Type: text/xml\r\nSOAPAction: urn:urn:oasis:names:tc:ebxml-msg:service/Acknowledgment\r\n\r\n";
        private const string ACKSERVICE = "urn:oasis:names:tc:ebxml-msg:service:Acknowledgment";

        private static string ebxmlacktemplate = null;

        private int port = DEFAULT_PORT;
        private string address = DEFAULT_ADDRESS;
        private X509Certificate certificate = null;
        private TcpListener listener = null;
        private bool keepGoing = false;
        private Dictionary<string, DateTime> receivedIds = null;

        private object requestLock = new object();
        private bool cleaning = false;

        /**
         * Listener on 0.0.0.0:443 
         */  
        internal Listener()
        {
            init();
        }

        /**
         * Listener on given IP address and port.
         */ 
        internal Listener(string a, int p)
        {
            port = p;
            address = a;
            init();
        }

        /**
         * Is this Listener active ?
         */ 
        public bool Listening
        {
            get { return (listener != null); }
        }

        private void init()
        {
            certificate = ConnectionManager.getInstance().getCertificate();
            if (ebxmlacktemplate == null)
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                StreamReader sr = new StreamReader(assembly.GetManifestResourceStream(EBXMLACKTEMPLATE));
                StringBuilder sb = new StringBuilder();
                String line = null;
                while ((line = sr.ReadLine()) != null)
                {
                    sb.Append(line);
                    sb.Append("\r\n");
                }
                ebxmlacktemplate = sb.ToString();
            }
            receivedIds = new Dictionary<string,DateTime>();
        }

        /**
         * Binds and starts an internal TcpListener for inbound requests, then enters an
         * accept() loop, until Listener.stop() is called.
         */ 
        internal void start()
        {
            try
            {
                if (listener != null)
                    return;
                listener = new TcpListener(IPAddress.Parse(address), port);
                listener.Start();
            }
            catch (Exception e)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sb = new StringBuilder("Exception starting Spine listener: ");
                sb.Append(e.ToString());
                logger.WriteEntry(sb.ToString(), EventLogEntryType.Error);
                throw e;
            }
            keepGoing = true;
            while (keepGoing)
            {
                Socket s = null;
                try
                {
                    s = listener.AcceptSocket();
                }
                catch (Exception e)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    StringBuilder sb = new StringBuilder("Exception accepting Spine connection: ");
                    sb.Append(e.ToString());
                    logger.WriteEntry(sb.ToString(), EventLogEntryType.Error);
                }
                Parallel.Invoke(() => this.processMessage(s));
            }
        }

        /**
         * Stops the internal TcpListener, and signals the accept() loop that it is time to stop.
         */           
        void stop()
        {
            keepGoing = false;
            if (listener != null)
            {
                try
                {
                    listener.Stop();
                    listener = null;
                }
                catch (Exception e)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    StringBuilder sb = new StringBuilder("Exception closing Spine listener: ");
                    sb.Append(e.ToString());
                    logger.WriteEntry(sb.ToString(), EventLogEntryType.Warning);
                }
            }
        }

        /**
         * Called from the ConnectionManager's retry processor to clean "persistDuration" expired
         * messages from the list of received ebXML message ids used for de-duplication.
         */ 
        internal void cleanDeduplicationList()
        {
            if (cleaning)
                return;
            cleaning = true;
            // We can't remove from the dictionary when we're enumerating the keys,
            // so build a list of expired ids and then go through that list removing
            // them from the received log.
            //
            List<string> expiredids = new List<string>();
            DateTime now = DateTime.Now;
            foreach (string id in receivedIds.Keys)
            {
                DateTime exp = receivedIds[id];
                if (now.CompareTo(exp) > 0)
                    expiredids.Add(id);
            }
            foreach (string id in expiredids)
            {
                try
                {
                    lock (requestLock)
                    {
                        receivedIds.Remove(id);
                    }
                }
                catch (Exception) { }
            }
            cleaning = false;
        }

        /**
         * Called in its own thread to process the inbound message on the given socket. Reads the
         * message, returns any synchronous or asynchronous acknowledgment, and logs the message id in the de-duplication list.
         * If the id has not been seen before (according to the list), calls ConnectionManager.receive().
         * 
         * @param Clear-text accepted socket
         */ 
        private void processMessage(Socket s)
        {
            // Log received ebXML message ids and add to it where "duplicate
            // elimination" is set. Do this in memory - if we go down between retries on long-
            // duration interactions we'll incorrectly not detect the duplicate, but that will
            // be a recognised limitation for now, and is supposed to be caught by HL7 de-
            // duplication anyway.
            //
            EbXmlMessage msg = null;
            try
            {
                ConnectionManager cm  = ConnectionManager.getInstance();
                NetworkStream n = new NetworkStream(s);
                SslStream ssl = new SslStream(n, false, validateRemoteCertificate, getLocalCertificate);

                // FIXME: Require client certificate - but leave it for now
                //

                ssl.AuthenticateAsServer(cm.getCertificate());
                msg = new EbXmlMessage(ssl);
                string ack = makeSynchronousAck(msg);
                if (ack != null)
                {
                    ssl.Write(Encoding.UTF8.GetBytes(ack));
                    ssl.Flush();
                }                    
                ssl.Close();

                // If we have data for it, DateTime.Now + persistDuration. The trouble is that
                // the persistDuration isn't actually carried in the message. Easiest is *probably*
                // to cache SDS details for "my party key" for all received messages, which will 
                // require a "tool" to do the calls.
                //
                Dictionary<string, TimeSpan> pd = cm.ReceivedPersistDurations;
                if (pd != null)
                {
                    bool notSeenBefore = true;
                    lock (requestLock)
                    {
                        notSeenBefore = (!receivedIds.ContainsKey(msg.Header.MessageId));
                    }
                    if (notSeenBefore)
                    {
                        try
                        {
                            // TimeSpan ts = pd[msg.SoapAction];
                            TimeSpan ts = pd[msg.Header.SvcIA];
                            DateTime expireTime = DateTime.Now;
                            expireTime = expireTime.Add(ts);
                            lock (requestLock)
                            {
                                receivedIds.Add(msg.Header.MessageId, expireTime) ;
                            }
                        }
                        catch (KeyNotFoundException) { }
                        cm.receive(msg);
                    }
                    // No "else" - if the message id is in the "receivedIds" set then we want
                    // to do the ack but nothing else.
                }
                else
                {
                    cm.receive(msg);
                }

            }
            catch (Exception e)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sb = new StringBuilder("Error receiving message from");
                sb.Append(s.RemoteEndPoint.ToString());
                sb.Append(" : ");
                sb.Append(e.ToString());
                logger.WriteEntry(sb.ToString(), EventLogEntryType.Error);
                if (msg != null)
                    doAsynchronousNack(msg);
                return;
            }
            doAsynchronousAck(msg);
        }

        /**
         * TODO: Constructs an asynchronous ebXML error message. Where do we get error information from ?
         * 
         * @param EbXml message to NACK
         */ 
        private void doAsynchronousNack(EbXmlMessage msg)
        {
            // If "msg" is an ack or message error
            if (msg.Header == null)
                return;
            // If "msg" is "unreliable"
            if (!msg.Header.DuplicateElimination)
                return;
            // If we've already returned the ack synchronously
            if (msg.Header.SyncReply)
                return;
        }

        /**
         * Checks to see if the given EbXmlMessage is acknowledged asynchronously, and just returns
         * if not. Otherwise, constructs an EbXMLAcknowledgment addressed to the sender of "msg", 
         * and calls the ConnectionManager to send it.
         * 
         * @param EbXmlMessage to acknowledge
         */ 
        private void doAsynchronousAck(EbXmlMessage msg)
        {
            if (msg == null)
                return;
            if (!msg.Header.DuplicateElimination)
                return;
            if (msg.Header.SyncReply)
                return;

            StringBuilder a = new StringBuilder(makeEbXmlAck(msg, false));
            ConnectionManager cm = ConnectionManager.getInstance(); 
            SDSconnection sds = cm.SdsConnection;
            string ods = msg.Header.FromPartyKey.Substring(0, msg.Header.FromPartyKey.IndexOf("-"));
            List<SdsTransmissionDetails> sdsdetails = sds.getTransmissionDetails(ACKSERVICE, ods, null, msg.Header.FromPartyKey);
            a.Replace("__CPA_ID__", sdsdetails[0].CPAid);
            EbXmlAcknowledgment ack = new EbXmlAcknowledgment(a.ToString());           
            ack.setHost(sdsdetails[0].Url);
            cm.send(ack, sdsdetails[0]);
        }

        /**
         * Checks to see if the EbXmlMessage should be acknowledged synchronously, and just returns
         * a zero-length string if not. Otherwise, makes an ebXML acknowledgment and returns it as
         * a string.
         * 
         * @param EbXmlMessage to acknowledge
         */ 
        private string makeSynchronousAck(EbXmlMessage msg)
        {
            if (!msg.Header.DuplicateElimination)
                return EMPTY_RESPONSE;
            if (!msg.Header.SyncReply)
                return EMPTY_RESPONSE;

            StringBuilder hdr = new StringBuilder(ACK_HTTP_HEADER);
            string ack = makeEbXmlAck(msg, true);
            hdr.Replace("__CONTENT_LENGTH__", ack.Length.ToString());
            hdr.Append(ack);
            return hdr.ToString();
        }

        /**
         * Assembles an ebXML acknowledgment.
         */ 
        private string makeEbXmlAck(EbXmlMessage msg, bool replaceCpaId)
        {
            StringBuilder sb = new StringBuilder(ebxmlacktemplate);
            sb.Replace("__FROM_PARTY_ID__", msg.Header.FromPartyKey);
            //sb.Replace("__FROM_PARTY_ID__", msg.Header.ToPartyKey);
            string mp = ConnectionManager.getInstance().SdsConnection.MyPartyKey;
            sb.Replace("__TO_PARTY_ID__", mp);
            sb.Replace("__ORIGINAL_MESSAGE_ID__", msg.getMessageId());
            sb.Replace("__CONVERSATION_ID__", msg.Header.ConversationId);
            if (replaceCpaId)
                sb.Replace("__CPA_ID__", msg.Header.CpaId);
            sb.Replace("__ACK_MESSAGE_ID__", System.Guid.NewGuid().ToString().ToUpper());
            sb.Replace("__ACK_TIMESTAMP__", DateTime.Now.ToString(EbXmlHeader.ISO8601DATEFORMAT));
            return sb.ToString();
        }

        /**
         * Delegate used when the SslStream actually used to talk to TMS, is created from the
         * underlying network connection. Returns the endpoint certificate to use for securing the
         * connection.
        */ 
        public X509Certificate getLocalCertificate(Object sender, string target, X509CertificateCollection localCerts, X509Certificate remoteCert, string[] acceptableIssuers)
        {
            return certificate;
        }

        /**
         * Delegate used when the SslStream actually used to talk to TMS, is created from the
         * underlying network connection. Used for mutual authentication to validate the Spine
         * certificate. 
         * 
         * Just now this returns true. It needs to do something more to verify that the Spine
         * certificate really is a certificate from Spine, rather than just one from another Spine
         * endpoint (i.e. one which would pass .Net's own checking the certificate chain against
         * known CAs).
         */ 
        public bool validateRemoteCertificate(Object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors err)
        {
            // TODO: Something more sensible with this
            return true;
        }
    }
}
