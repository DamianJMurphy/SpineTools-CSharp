using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
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
     * Singleton class to initialise and manage resources used sending and receiving messages
     * to and from TMS. Configured via registry entries.
     */ 
    public class ConnectionManager
    {
        internal const string CONNECTION_MANAGER_REGSITRY_KEY = "HKEY_LOCAL_MACHINE\\Software\\HSCIC\\TMSConnectionManager";
        private const string CERT_FILE_REGVAL = "EndpointCertificate";
        internal const string CERT_PASS_REGVAL = "CertPass";
        private const string PASSWORD_PROVIDER = "PasswordProvider";
        private const string MESSAGE_DIRECTORY_REGVAL = "MessageDirectory";
        private const string EXPIRED_DIRECTORY_REGVAL = "ExpiredDirectory";
        private const string RETRY_TIMER_PERIOD_REGVAL = "RetryTimerPeriod";
        private const string PERSIST_DIRATION_FILE_REGVAL = "PersistDurations";
        private const string USE_NULL_DEFAULT_SYNCHRONOUS_HANDLER_REGVAL = "UseNullDefaultSynchronousHandler";
        private const string MY_IP_REGVAL = "MyIp";
        public const string LOGSOURCE = "Spine ConnectionManager";
        private static char[] TABSEPARATOR = { '\t' };
        private const int HTTPS = 443;

        private Listener listener = null;

        private static ConnectionManager me = new ConnectionManager();
        private static Exception bootException = null;
        private X509Certificate2 endpointCertificate = null;
        private string messageDirectory = null;
        private string expiredDirectory = null;
        private string myIp = null;

        private X509CertificateCollection endpointCertificateCollection = null;

        private IPasswordProvider passwordProvider = null;

        /**
         * Reference to SDS connection handler for endpoint lookups.
         */ 
        private SDSconnection sdsConnection = null;

        /**
         * Timer for the process that handles ebXML retries, and expiring unacknowledged transmissions
         * that have exceeded their persistDuration
         */ 
        private Timer retryProcessorTimer = null;
        private const long DEFAUlTRETRYCHECKINTERVAL = 30000;
        private long retryCheckPeriod = DEFAUlTRETRYCHECKINTERVAL;

        /**
         * "Reliable" ebXML requests that have not yet been acknowledged, keyed on message id.
         */ 
        private Dictionary<string, Sendable> requests = null;

        /**
         * "Handler" implementations for received Spine messages (in this version, these are all for received
         * asynchronous Spine responses, or other ebXML notifications). Keyed on SOAPaction derived from the
         * SDS "SVCIA" value.
         */ 
        private Dictionary<string, ISpineHandler> handlers = null;

        private ISynchronousResponseHandler defaultSynchronousResponseHandler = null;
        private ISpineHandler defaultSpineHandler = null;

        /**
         * For any cases where a "reliable" ebXML message expires its persistDuration, a handler can be
         * provided to take care of any subsequent actions (beyond the default of just logging it, saving
         * the message, and stopping any more automatic retries). Keyed on the SOAPaction derived from the
         * SDS SVCIA value.
         */ 
        private Dictionary<string, IExpiredMessageHandler> expiryHandlers = null;

        /*
         * To avoid having to do lookups on SDS (even in the cache) for inbound messages that we may not 
         * have seen before, this contains a pre-canned list of persistDuration time spans, keyed on
         * the SOAPaction derived from the SDS SVCIA values.
         */ 
        private Dictionary<string, TimeSpan> persistDurations = null;
        private const string INTERNAL_PERSIST_DURATIONS = "SpineTools.Connection.persistDurations.txt";
        /**
         * For synchronous Spine requests, this is a handler keyed on the SOAPaction of the outbound request 
         */ 
        private Dictionary<string, ISynchronousResponseHandler> synchronousHandlers = null;

        /**
         * Used for lock mutexes only.
         */ 
        private object requestLock = new object();

        private ITKTrunkHandler itkTrunkHandler = null;

        /**
         * Singleton constructor.
         */ 
        private ConnectionManager()
        {
            getPasswordProvider();
            requests = new Dictionary<string, Sendable>();
            expiryHandlers = new Dictionary<string, IExpiredMessageHandler>();
            handlers = new Dictionary<string, ISpineHandler>();
            synchronousHandlers = new Dictionary<string, ISynchronousResponseHandler>();
            itkTrunkHandler = new ITKTrunkHandler();
            handlers.Add("urn:nhs:names:services:itk/COPC_IN000001GB01", itkTrunkHandler);
            handlers.Add("\"urn:nhs:names:services:itk/COPC_IN000001GB01\"", itkTrunkHandler);
            loadCertificate();
            messageDirectory = (string)Registry.GetValue(CONNECTION_MANAGER_REGSITRY_KEY, MESSAGE_DIRECTORY_REGVAL, "");
            if (messageDirectory.Length == 0)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                logger.WriteEntry("No message directory provided - only in-memory persistence available", EventLogEntryType.Warning);
            }
            // Make sure messageDirectory is terminated with a path delimiter, because we're going
            // to need to add message ids to it when we call depersist() to delete persisted messages
            // on expiry or explicit transmission result.
            //
            if (!messageDirectory.EndsWith("\\"))
                messageDirectory = messageDirectory + "\\";

            myIp = (string)Registry.GetValue(CONNECTION_MANAGER_REGSITRY_KEY, MY_IP_REGVAL, "");
            if (myIp.Length == 0)
            {
                myIp = null;
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                logger.WriteEntry("No local IP address provided - will use first non-localhost interface", EventLogEntryType.Warning);
            }
            expiredDirectory = (string)Registry.GetValue(CONNECTION_MANAGER_REGSITRY_KEY, EXPIRED_DIRECTORY_REGVAL, "");
            if (expiredDirectory.Length == 0)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                logger.WriteEntry("No expired message directory provided - administrative handling of unsent messages NOT available", EventLogEntryType.Warning);
            } 
            sdsConnection = new SDSconnection();
            try
            {
                retryCheckPeriod = Int64.Parse((string)Registry.GetValue(CONNECTION_MANAGER_REGSITRY_KEY, RETRY_TIMER_PERIOD_REGVAL, ""));
            }
            catch { }

            string nulldefault = (string)Registry.GetValue(CONNECTION_MANAGER_REGSITRY_KEY, USE_NULL_DEFAULT_SYNCHRONOUS_HANDLER_REGVAL, "");
            if (nulldefault.ToLower().StartsWith("y"))
                defaultSynchronousResponseHandler = new NullSynchronousResponseHandler();
            else
                defaultSynchronousResponseHandler = new DefaultFileSaveSynchronousResponseHandler();
            defaultSpineHandler = new DefaultFileSaveSpineHandler();

            persistDurations = loadReceivedPersistDurations();
            if (retryCheckPeriod != 0)
                retryProcessorTimer = new Timer(processRetries, null, retryCheckPeriod, retryCheckPeriod);
        }

        private void getPasswordProvider()
        {
            string pp = (string)Registry.GetValue(CONNECTION_MANAGER_REGSITRY_KEY, PASSWORD_PROVIDER, "");
            if (pp.Length == 0)
            {
                passwordProvider = new DefaultRegistryReadPasswordProvider();
                return;
            }
            Assembly a = Assembly.Load(new AssemblyName("SpineTools"));
            passwordProvider = (IPasswordProvider)a.CreateInstance(pp);
        }

        internal Dictionary<string, TimeSpan> ReceivedPersistDurations
        {
            get { return persistDurations; }
        }

        /**
         * Get ITK Trunk Handler instance so DistributionEnvelope handlers
         * can be added to it.
         */ 
        public ITKTrunkHandler ItkTrunkHandler
        {
            get { return itkTrunkHandler; }
        }
        /**
         * "My" IP address for the "from" and "reply to" elements of synchronous Spine queries.
         */ 
        public string MyIp
        {
            get { return myIp; }
        }

        private Dictionary<string, TimeSpan> loadReceivedPersistDurations()
        {
            string pdFile = (string)Registry.GetValue(CONNECTION_MANAGER_REGSITRY_KEY, PERSIST_DIRATION_FILE_REGVAL, "");
            StreamReader sr = null;
            if (pdFile.Length == 0)
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                sr = new StreamReader(assembly.GetManifestResourceStream(INTERNAL_PERSIST_DURATIONS));
            }
            else {
                try
                {
                    sr = new StreamReader(new FileStream(pdFile, FileMode.Open));
                }
                catch (Exception e)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    StringBuilder sbe = new StringBuilder("Cannot read inbound persistDuration data: ");
                    sbe.Append(e.ToString());
                    sbe.Append("ebXml duplicate elimination NOT available");
                    logger.WriteEntry(sbe.ToString(), EventLogEntryType.Warning);
                    return null;
                }            
            }
            Dictionary<string, TimeSpan> pd = new Dictionary<string, TimeSpan>();
            string line = null;
            while ((line = sr.ReadLine()) != null)
            {
                try
                {
                    string[] fields = line.Split(TABSEPARATOR);
//                    char[] f = fields[0].ToCharArray();
//                    f[fields[0].LastIndexOf(':')] = '/';
//                    fields[0] = new string(f);
                    int s = SdsTransmissionDetails.iso8601DurationToSeconds(fields[1]);
                    TimeSpan t = new TimeSpan(0, 0, s);
                    pd.Add(fields[0], t);
                }
                catch (Exception e)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    StringBuilder sbe = new StringBuilder("Cannot read inbound persistDuration data: ");
                    sbe.Append(e.ToString());
                    sbe.Append("ebXml duplicate elimination NOT available");
                    logger.WriteEntry(sbe.ToString(), EventLogEntryType.Warning);
                    return null;
                }
            }
            sr.Close();
            return pd;
        }

        public TimeSpan getPersistDuration(string s)
        {
            if (!persistDurations.ContainsKey(s))
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sbe = new StringBuilder("No persistDuration data available for: ");
                sbe.Append(s);
                sbe.Append("ebXml defaulting to ZERO");
                logger.WriteEntry(sbe.ToString(), EventLogEntryType.Warning);
                return TimeSpan.Zero;
            }
            return persistDurations[s];
        }

        /**
         * Registers an instance of a synchronous response handler, against the SOAPaction of the
         * request message.
         */ 
        public void addSynchronousResponseHandler(string sa, ISynchronousResponseHandler h)
        {
            synchronousHandlers.Add(sa, h);
        }

        /**
         * Registers an instance of a reliable asynchronous message expiry handler, against the 
         * SOAPaction of the message.
         */ 
        public void addExpiryHandler(string sa, IExpiredMessageHandler h)
        {
            expiryHandlers.Add(sa, h);
        }

        /**
         * Gets any expired message handler for the given SOAPaction, or null if none if defined.
         */ 
        public IExpiredMessageHandler getExpiryHandler(string sa)
        {
            if (!expiryHandlers.ContainsKey(sa))
                return null;
            return expiryHandlers[sa];
        }

        /**
         * Used for processing asynchronous acknowledgments. If the given message id is known
         * (i.e. if we have a request for it) then it is removed. Otherwise the unknown id is
         * logged. This is only called by the TMS listener at present, though in principle it
         * could be called for other transports.
         * 
         * @param ebXml message id of the received acknowledgement or error notification
         */ 
        public void registerAck(string a)
        {
            if (a == null)
                return;
            if (requests.ContainsKey(a))
            {
                requests.Remove(a);
            }
            else
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                logger.WriteEntry("Ack/Nack received for unknown message id " + a, EventLogEntryType.Error);
            }
        }

        /**
         * Registers a handler for a received Spine message, agains the SOAPaction under which it is 
         * delivered.
         */ 
        public void addHandler(string s, ISpineHandler h)
        {
            handlers.Add(s, h);
        }

        /**
         * Gets the current list of Spine message handlers.
         */ 
        public Dictionary<string, ISpineHandler> Handlers
        {
            get { return handlers; }
        }

        /**
         * Returns the SDSconnection class for making queries against SDS.
         */ 
        public SDSconnection SdsConnection
        {
            get { return sdsConnection; }
        }

        /**
         * Internal initialisation - load the Spine endpoint certificate.
         */ 
        private void loadCertificate()
        {
            string cf = (string)Registry.GetValue(CONNECTION_MANAGER_REGSITRY_KEY, CERT_FILE_REGVAL, "");
            string cp = "";
            if (passwordProvider != null)
            {
                cp = passwordProvider.getPassword();
            }
            if (cf.Length > 0)
            {
                try
                {
                    endpointCertificate = new X509Certificate2(cf, cp);
                }
                catch (Exception e)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    StringBuilder sb = new StringBuilder("Certificate ");
                    sb.Append(cf);
                    sb.Append(" failed to load: ");
                    sb.Append(e.ToString());
                    sb.Append(" - no messaging possible");
                    logger.WriteEntry(sb.ToString(), EventLogEntryType.Error);
                    bootException = e;
                }
            }
            else
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                logger.WriteEntry("No certificate provided - only clear text messaging possible", EventLogEntryType.Warning);
            }
            endpointCertificateCollection = new X509CertificateCollection();
            endpointCertificateCollection.Add(endpointCertificate);
        }


        /**
         * Singleton getInstance() method
         */ 
        public static ConnectionManager getInstance()
        {
            if (bootException != null)
                throw bootException;
            return me;
        }

        /**
         * Wrapper round the transmission and, where relevant retry and receipt of an asynchronous
         * response, for a "Sendable" message using the given transmission details resolved either
         * from SDS or the local SDS cache. This does the actual transmission in a separate thread
         * so will return immediately.
         * 
         * @param Sendable s Message to send
         * @param SdsTransmissionDetails c retrieved transmission details for the message type/recipient pair
         */ 
        public void send(Sendable s, SdsTransmissionDetails c)
        {
            if (!c.IsSynchronous)
            {
                listen();
                // Don't expect an ack if we're sending an asynchronous ack, but otherwise do
                // do that we have something to attach a response to, if we're going to get one.
                //
                if (!(s.Type == Sendable.ACK) && (c.DuplicateElimination.Equals("always")))
                {
                    if (!requests.ContainsKey(s.getMessageId()))
                        requests.Add(s.getMessageId(), s);
                }
            }
            (new Thread(() => this.doTransmission(s))).Start();
        }

        /**
         * Delegate called by a timer kicked off in the constructor.
        */
        private void processRetries(object stateInfo)
        {
            if ((requests == null) || (requests.Count == 0))
            {
                listener.cleanDeduplicationList();
                return;
            }
            DateTime check = DateTime.Now;
            foreach (Sendable s in requests.Values)
            {
                // If s.Started is more than persistDuration ago, remove
                // from "requests" and call s.Expire()
                //
                TimeSpan tsExpire = new TimeSpan(0, 0, s.PersistDuration);                
                DateTime expiryTime = s.Started.Add(tsExpire);
                if (expiryTime.CompareTo(check) < 0)
                {
                    lock (requestLock)
                    {
                        try
                        {
                            removeRequest((EbXmlMessage)s);
                            s.Expire();
                        }
                        catch (Exception) { }
                    }
                }
                else
                {
                    // Make sure that we're at least retryInterval since the
                    // last time, and if we are, then re-do the transmission.
                    //
                    DateTime lt = s.LastTry;
                    if (lt == DateTime.MinValue)
                        return;
                    TimeSpan tsRetry = new TimeSpan(0, 0, s.RetryInterval);
                    DateTime retryAfter = lt.Add(tsRetry);
                    if (retryAfter.CompareTo(check) < 0)
                        (new Thread(() => this.doTransmission(s))).Start();
                }
            }
            listener.cleanDeduplicationList();
        }

        /**
         * Gets the directory where messages that have expired their persistDuration, are copied.
         */ 
        public string ExpiredMessagesDirectory
        {
            get { return expiredDirectory; }
        }

        /**
         * Internal method to transmit a Sendable message, called by send() and by the retry mechanism.
         * This handles:
         * - de-queueing reliable messages that have been acknowledged synchronously (or for which an explicit error has been received), 
         * - collecting any synchronous response which is stored in the SynchronousResponse property of the Sendable instance.
         * - calling the response handler for synchronous requests
         * - error logging
         * 
         * Note that due to a design error in SDS not all target endpoint URLs are resolvable from SDS:
         * "forwarded" messages for which Spine is an intermediary must resolve the endpoint URL some
         * other way. See the SDSconnection API documentation for how this is configured.
         * 
         * @param Message to transmit
         */ 
        private void doTransmission(Sendable s)
        {
            // Create a socket and then do the TLS over it to
            // make an SSL stream. Then write the Sendable down the stream and get any
            // "stuff" back synchronously. If we do get a synchronous ack, remove the
            // reference to the transmitted message from "requests".
            //
            Socket clearSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // Note this uses the "ResolvedUrl" from the Sendable, not the "Url" from SDS because the
            // SDS data will be broken by design for "forwarded" services.
            //

            // TODO NEXT: Persist and send from persisted message
            string host = null;
            string syncresponse = null;
            if (s.ResolvedUrl == null)
            {
                // Sending persisted, reloaded message
                host = ((EbXmlMessage)s).getHost();
                clearSocket.Connect(host, HTTPS);
            }
            else
            {
                // "Primary" transmission
                s.persist();
                Uri uri = new Uri(s.ResolvedUrl);
                host = uri.Host;
                if (uri.Port == -1)
                    clearSocket.Connect(uri.Host, HTTPS);
                else
                    clearSocket.Connect(host, uri.Port);             
            }
            if (!s.recordTry())
            {
                if (s.getMessageId() != null)
                {
                    removeRequest((EbXmlMessage)s);
                    s.Expire();
                }
                return;
            }
            string httpResponseLine = null;
            using (NetworkStream n = new NetworkStream(clearSocket))
            {
                SslStream ssl = new SslStream(n, false, validateRemoteCertificate, new LocalCertificateSelectionCallback(getLocalCertificate));
                //ssl.AuthenticateAsClient(host);
                ssl.AuthenticateAsClient(host, endpointCertificateCollection, SslProtocols.Tls, true);
                if (!ssl.IsAuthenticated || !ssl.IsSigned || !ssl.IsEncrypted)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    logger.WriteEntry("Failed to authenticate SSL connection", EventLogEntryType.Error);
                    throw new Exception("Failed to authenticate SSL connection");
                }
                s.write(ssl);
                ssl.Flush();
                // Read any response ...
                // ... first line first...
                httpResponseLine = readline(ssl);
                // ... then the rest of it
                int contentlength = getHeader(ssl);
                if (contentlength == -1)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    logger.WriteEntry("Failed to read response content length", EventLogEntryType.Error);
                    return;
                }
                // Process response and add it to the Sendable - Spine synchronous responses are all fairly
                // small, even for with-history retrievals 
                if (contentlength > 0)
                {
                    int read = 0;
                    byte[] buffer = new byte[contentlength];
                    do
                    {
                        int r = ssl.Read(buffer, read, contentlength - read);
                        if (r == -1)
                        {
                            EventLog logger = new EventLog("Application");
                            logger.Source = LOGSOURCE;
                            logger.WriteEntry("Premature EOF sending " + s.getMessageId(), EventLogEntryType.Error);
                            break;
                        }
                        read += r;
                    } while (read < contentlength);
                    syncresponse = Encoding.UTF8.GetString(buffer);
                }                
            }
            s.SyncronousResponse = syncresponse;
            if (s.Type == Sendable.SOAP)
            {
                if (synchronousHandlers.ContainsKey(s.SoapAction))
                {
                    ISynchronousResponseHandler h = synchronousHandlers[s.SoapAction];
                    h.handle((SpineSOAPRequest)s);
                }
                else
                {
                    defaultSynchronousResponseHandler.handle((SpineSOAPRequest)s);
                }
                return;
            }
            // "Don't listen for any response" conditions where we remove this message from the
            // current set of things-we're-waiting-on-responses-for are:
            // Explicit error (HTTP 500)
            // A synchronous ebXML response (Acknowledgement or MessageError) with our message id in it.
            //
            // Note: The contract properties in SDS are a mess, so don't bother trying to infer behaviour
            // from them.
            //
            if (s.getMessageId() != null) // Don't try for acks.
            {
                if (s.SyncronousResponse != null)
                {
                    if (httpResponseLine.Contains("HTTP 5") || (s.SyncronousResponse.Contains(s.getMessageId())))
                        removeRequest((EbXmlMessage)s);
                }
            }
            
        }

        /**
         * Run the TMS listener for asynchronous responses or other inbound connections, if it is not
         * already started. The listener is run in its own thread.
         */ 
        public void listen()
        {
           if (listener == null)
                listener = new Listener();
           if (!listener.Listening)
               (new Thread(() => listener.start())).Start();
        }

        /**
         * Reads the HTTP header of a synchronous response, and returns the content length.
         * 
         * @param SslStream on which to read the synchronous response header
         */ 
        private int getHeader(SslStream s)
        {
            // Read the header, return the content length. Header ends when readLine returns
            // a zero-length string.
            string line = null;
            int clen = -1;
            do
            {
                line = readline(s);
                if (line == null)
                    break;
                if (line.ToLower().Contains("content-length: "))
                {
                    string length = line.Substring("content-length: ".Length).Trim();
                    clen = Convert.ToInt32(length);
                }
            }
            while (line.Length != 0);
            return clen;
        }

        private void depersist(string m)
        {
            string[] files = Directory.GetFiles(messageDirectory, "*_" + m);
            EventLog logger = null;
            StringBuilder sbe = null;
            switch (files.Length)
            {
                case 0:
                    logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    sbe = new StringBuilder("Unexpected error de-persisting message ");
                    sbe.Append(m);
                    sbe.Append(" cannot find persisted message id");
                    logger.WriteEntry(sbe.ToString(), EventLogEntryType.Warning);
                    break;

                case 1:
                    string pfile = messageDirectory + files[0];
                    try
                    {
                        File.Delete(pfile);
                    }
                    catch (Exception e)
                    {
                        logger = new EventLog("Application");
                        logger.Source = LOGSOURCE;
                        sbe = new StringBuilder("Unexpected error ");
                        sbe.Append(e.ToString());
                        sbe.Append(" de-persisting message ");
                        sbe.Append(pfile);
                        logger.WriteEntry(sbe.ToString(), EventLogEntryType.Error);
                    }
                    return;
                
                default:
                    logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    sbe = new StringBuilder("Unexpected error de-persisting message ");
                    sbe.Append(m);
                    sbe.Append(" multiple instances of persisted message id");
                    logger.WriteEntry(sbe.ToString(), EventLogEntryType.Error);
                    break;
            }
        }

        private void depersist(EbXmlMessage a)
        {
            string pfile = messageDirectory + a.getOdsCode() + "_" + a.getMessageId();
            try
            {
                if (File.Exists(pfile))
                    File.Delete(pfile);
            }
            catch (Exception e)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sbe = new StringBuilder("Unexpected error ");
                sbe.Append(e.ToString());
                sbe.Append(" de-persisting message ");
                sbe.Append(pfile);
                logger.WriteEntry(sbe.ToString(), EventLogEntryType.Error);
            }
        }

        /**
         * Internal method to read HTTP response header lines. Used by getHeader()
         */ 
        private string readline(SslStream s)
        {
            StringBuilder sb = new StringBuilder();
            Int32 i = 0;
            char c = (char)0;
            try
            {
                do
                {
                    i = s.ReadByte();
                    if (i == -1)
                        break;
                    c = Convert.ToChar(i);
                    if (c == '\n')
                        break;
                    if (c != '\r')
                        sb.Append(c);
                }
                while (true);
            }
            catch (Exception e)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sbe = new StringBuilder("Unexpected error ");
                sbe.Append(e.ToString());
                sbe.Append(" reading response header.");
                logger.WriteEntry(sbe.ToString(), EventLogEntryType.Error);
            }
            return sb.ToString();
        }

        /** Called at start-up if the system needs to load any persisted, reliable messages
         * for sending. This applies the persist duration for the message type to the declared
         * timestamp, and will run the expire() method on anything that has expired whilst
         * the MHS was down.
         */
        public void loadPersistedMessages()
        {
            string[] files = Directory.GetFiles(messageDirectory);
            EbXmlMessage ebxml = null;
            foreach (String f in files)
            {
                using (FileStream fs = new FileStream(f, FileMode.Open))
                {
                    try
                    {
                        ebxml = new EbXmlMessage(fs);

                        // "f" is now of the form "odscode_messageid" so get the ods code and set it here.
                        string ods = f.Substring(0, f.IndexOf("_")).Substring(f.LastIndexOf("\\") + 1);
                        ebxml.setOdsCode(ods);

                        // Do an SDS lookup and populate retry interval, persist duration
                        // and retry cound in ebxml.

                        List<SdsTransmissionDetails> sdsdetails = sdsConnection.getTransmissionDetails(ebxml.Header.SvcIA, ods, ebxml.HL7Message.ToAsid, ebxml.Header.ToPartyKey);
                        if (sdsdetails.Count == 0)
                            throw new Exception("Cannot resolve SDS details for persisted message: " + f);
                        if (sdsdetails.Count > 1)
                            throw new Exception("Ambiguous SDS details for persisted message: " + f);

                        SdsTransmissionDetails sds = sdsdetails[0];
                        ebxml.PersistDuration = sds.PersistDuration;
                        ebxml.RetryCount = sds.Retries;
                        ebxml.RetryInterval = sds.RetryInterval;
                    }
                    catch(Exception e)
                    {
                        EventLog logger = new EventLog("Application");
                        logger.Source = LOGSOURCE;
                        StringBuilder sbe = new StringBuilder("Failed to load persisted ebXml message ");
                        sbe.Append(f);
                        sbe.Append(" due to exception ");
                        sbe.Append(e.Message);
                        sbe.Append(" at ");
                        sbe.Append(e.StackTrace);
                        logger.WriteEntry(sbe.ToString(), EventLogEntryType.FailureAudit);
                        continue;
                    }
                }
                DateTime check = DateTime.Now;
                TimeSpan pd = getPersistDuration(ebxml.Header.SvcIA);
                DateTime expiryTime = ebxml.Started.Add(pd);
                if (expiryTime.CompareTo(check) < 0)
                {
                    depersist(ebxml);
                    ebxml.Expire();
                }
                else
                {
                    requests.Add(ebxml.getMessageId(), ebxml);
                }
            }
        }


        /**
         * Removes a reliable request from the retry list. Does nothing if null is passed
         * or if the message id is not known to the retry list.
         * 
         * @param a Message id of the request to remove.
         */ 
        internal void removeRequest(EbXmlMessage a)
        {
            if (a == null)
                return;
            string m = a.getMessageId();
            if (requests.ContainsKey(m))
            {
                requests.Remove(m);
                depersist(a);
            }
        }

        /** Called by the Listener to process a received message. See if this
         * is a response and correlate with requests if it is, otherwise add
         * to the received messages queue. This is called by Listener.processMessage()
         * which is invoked in its own thread, so we don't need to spawn any new
         * threads here.
         * 
         * @param Received message
        */
        internal void receive(Sendable s)
        {

            if (s.SoapAction.Contains("service:Acknowledgment") || s.SoapAction.Contains("service:MessageError"))
            {
                // Asynchronous ack/nack. Remove from request list and exit.
                //
                EbXmlMessage m = (EbXmlMessage)s;
                if (!requests.Remove(m.Header.ConversationId))
                {
                    // Log receipt of ack/nack that doesn't belong to us...
                    // Note: This may be legitimate in a clustered MHS or if in- and out-bound
                    // nodes are separate.
                    //
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    StringBuilder sbe = new StringBuilder("Unexpected response ");
                    sbe.Append(s.SoapAction);
                    sbe.Append(" with conversation id ");
                    sbe.Append(m.Header.ConversationId);
                    sbe.Append(" that was not sent from here.");
                    logger.WriteEntry(sbe.ToString(), EventLogEntryType.Information);
                }

                depersist(m.Header.ConversationId);
                return;
            }
            ISpineHandler h = null;
            try
            {
                h = handlers[s.SoapAction];
            }
            catch (KeyNotFoundException)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sbe = new StringBuilder("Unknown SOAP action ");
                sbe.Append(s.SoapAction);
                sbe.Append(" using DefaultFileSaveSpineHandler");
                logger.WriteEntry(sbe.ToString(), EventLogEntryType.FailureAudit);
                h = defaultSpineHandler;
            }
            try
            {
                h.handle(s);
            }
            catch (Exception e)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sbe = new StringBuilder("Exception handling  ");
                sbe.Append(s.SoapAction);
                sbe.Append(" : ");
                sbe.Append(e.ToString());
                logger.WriteEntry(sbe.ToString(), EventLogEntryType.FailureAudit);
            }
        }

        /**
         * Returns the directory for persisting reliable messages.
         */ 
        public string getMessageDirectory() { return messageDirectory; }

        /**
         * Returns the endpoint certificate
         */ 
        internal X509Certificate getCertificate()
        {
            return endpointCertificate;
        }

        /**
         * Delegate used when the SslStream actually used to talk to TMS, is created from the
         * underlying network connection. Returns the endpoint certificate to use for securing the
         * connection.
         */ 
        public X509Certificate getLocalCertificate(Object sender, string target, X509CertificateCollection localCerts, X509Certificate remoteCert, string[] acceptableIssuers)
        {
            return endpointCertificate;
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
            // FIXME: To do something more sensible with this needs some information on the certificate
            // we're being asked to validate. *Currently* we're happy that the chain is good and that
            // we've installed the Spine root and sub CAs so that the chain validation is OK. But consider
            // any additional checks we might want to do. In particular, consider off-line CRL checking 
            // since Spine CRLs are hidden away in an LDAPS directory and aren't very accessible.
            //
            return true;
        }

    }
}
