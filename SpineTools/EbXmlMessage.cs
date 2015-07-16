using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using SpineTools.Connection;
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
namespace SpineTools
{
    /**
     * Concrete Sendable class representing a Spine asynchronous ebXML message.
     */ 
    public class EbXmlMessage : Sendable
    {
        private const string FIRSTMIMEPREFIX = "--";
        private const string MIMEPREFIX = "\r\n--";
        private const string MIMEPOSTFIX = "--";
        private const int MAX_MESSAGE_SIZE = 5242880; // 5MB Spine maximum message size for ebXML messages
        private const string HTTPHEADER = "POST __CONTEXT_PATH__ HTTP/1.1\r\nHost: __HOST__\r\nSOAPAction: \"__SOAP_ACTION__\"\r\nContent-Length: __CONTENT_LENGTH__\r\nContent-Type: multipart/related; boundary=\"__MIME_BOUNDARY__\"; type=\"text/xml\"; start=\"<__START_ID__>\"\r\nConnection: close\r\n\r\n";

        private EbXmlMessage response = null;
        private EbXmlHeader header = null;
        private SpineHL7Message hl7message = null;
        private List<Attachment> attachments = null;
        private string mimeboundary = "--=_MIME-Boundary";

        private bool persistable = false;
        private bool hasBeenPersisted = false;
        private string persistenceFile = null;
        private string receivedHost = "SPINE_RELIABLE_MESSAGE_HOST";
        private string receivedContextPath = "EXPIRED_PERSISTED_RELIABLE_MESSAGE";

        /**
         * Used to assemble an EbXmlMessage from the given Stream, which has been accepted from the network.
         * This expects to get the inbound HTTP stream (or at least an SslStream) from the start, because it
         * begins processing with the HTTP POST.
         */ 
        public EbXmlMessage(Stream instream) 
        {
            // Assemble from network - note that Spine messages are NOT chunked
            //
            string headerline = null;
            Dictionary<string, string> headers = new Dictionary<string, string>();
            while ((headerline = readLine(instream)).Trim().Length != 0)
            {
                if (headerline.StartsWith("POST"))
                {
                    int firstSpace = headerline.IndexOf(' ');
                    int secondSpace = headerline.IndexOf(' ', firstSpace + 1);
                    if ((firstSpace == -1) || (secondSpace == -1))
                        throw new Exception("Malformed HTTP request line, can't parse POST context path");
                    receivedContextPath = headerline.Substring(firstSpace, secondSpace - firstSpace);
                }
                else
                {
                    int colon = headerline.IndexOf(":");
                    if (colon == -1)
                    {
                        throw new Exception("Malformed HTTP header - no field/data delimiter colon");
                    }
                    headers.Add(headerline.Substring(0, colon).ToUpper(), headerline.Substring(colon + 1).Trim());
                }
            }
            string ctype = headers["CONTENT-TYPE"];
            if (ctype == null) {
                throw new Exception("Malformed HTTP headers - no Content-Type found");
            }
            if (ctype.Contains("multipart/related")) {
                mimeboundary = setMimeBoundary(ctype);
            }
            receivedHost = headers["HOST"];
            string clen = headers["CONTENT-LENGTH"];
            if (clen == null)
            {
                throw new Exception("Malformed HTTP headers - no Content-Length found");
            }
            int contentlength = Int32.Parse(clen);
            soapAction = headers["SOAPACTION"];
            soapAction = soapAction.Replace('"', ' ').Trim();

            // There is a bug in Spine-hosted services that turns a SOAPaction starting with
            // "urn:" into "urn:urn:" - fix this if we find it.
            //
            if (soapAction.StartsWith("urn:urn:"))
                soapAction = soapAction.Substring(4);

            // Read content-length bytes and parse out the various parts of the 
            // received message. 
            // TODO: Put in proper handling.
            //
            Byte[] wire = new Byte[contentlength];
            int bytesRead = 0;
            while (bytesRead < contentlength)
            {
                bytesRead += instream.Read(wire, bytesRead, contentlength - bytesRead);
            }
            string msg = Encoding.UTF8.GetString(wire);

            // Split on the mimeboundary. "msg" doesn't contain the HTTP headers so we should
            // just be able to walk through the attachments. If we can't, report an exception
            //
            int startPosition = msg.IndexOf(mimeboundary, 0);
            if (startPosition == -1)
            {
                // Need to handle the case where the content is
                // actually an asynchronous ebXML ack.
                //
                // If content-type is text/xml and soapaction contains Acknowledgment or MessageError,
                // it is an ack/nack. If we get one of these we need to log it and tell the connection
                // manager about it. But we don't need to do any further processing.
                //
                if (ctype.ToLower().StartsWith("text/xml"))
                {
                    if (soapAction.Contains("Acknowledgment"))
                    {
                        // Remove from requests, and exit
                        string a = EbXmlAcknowledgment.getAckedMessageId(msg);
                        if (a == null)
                        {
                            EventLog logger = new EventLog("Application");
                            logger.Source = ConnectionManager.LOGSOURCE;
                            StringBuilder sb = new StringBuilder("Failed to extract message id reference from Acknowledgment: ");
                            sb.Append(msg);
                            logger.WriteEntry(sb.ToString(), EventLogEntryType.Error);
                            return;
                        }
                        ConnectionManager cm = ConnectionManager.getInstance();
                        cm.registerAck(a);
                        return;
                    }
                    if (soapAction.Contains("MessageError"))
                    {
                        // Remove from requests, and exit
                        string a = EbXmlAcknowledgment.getAckedMessageId(msg);
                        if (a == null)
                        {
                            EventLog logger = new EventLog("Application");
                            logger.Source = ConnectionManager.LOGSOURCE;
                            StringBuilder sb = new StringBuilder("Failed to extract message id reference from MessageError: ");
                            sb.Append(msg);
                            logger.WriteEntry(sb.ToString(), EventLogEntryType.Error);
                            return;
                        }
                        EventLog l = new EventLog("Application");
                        l.Source = ConnectionManager.LOGSOURCE;
                        StringBuilder sbe = new StringBuilder("EbXML MessageError received: ");
                        sbe.Append(msg);
                        l.WriteEntry(sbe.ToString(), EventLogEntryType.Error);
                        
                        ConnectionManager cm = ConnectionManager.getInstance();
                        cm.registerAck(a);
                        return;
                    }
                }
                throw new Exception("Malformed message");
            }
            int endPosition = 0;
            int partCount = 0;
            bool gotEndBoundary = false;
            do
            {
                startPosition += mimeboundary.Length;
                endPosition = msg.IndexOf(mimeboundary, startPosition);
                if (endPosition == -1)
                {
                    gotEndBoundary = true;
                }
                else
                {
                    switch (partCount)
                    {
                        case 0:
                            header = new EbXmlHeader(msg.Substring(startPosition, endPosition - startPosition));
                            if (header.Timestamp != null)
                            {
                                started = DateTime.Parse(header.Timestamp);
                                // We don't know how many attempts were actually made, so assume
                                // only one try at the start time.
                                //
                                lastTry = DateTime.Parse(header.Timestamp);
                                tries = 1;
                            }
                            break;
                        case 1:
                            hl7message = new SpineHL7Message(msg.Substring(startPosition, endPosition - startPosition));
                            break;
                        default:
                            if (attachments == null)
                                attachments = new List<Attachment>();
                            // IMPROVEMENT: Make this more flexible to be able to support multiple types of
                            // ITK trunk message, just in case
                            //
                            if (soapAction.Contains("COPC_IN000001GB01"))
                            {
                                attachments.Add(new ITKDistributionEnvelopeAttachment(msg.Substring(startPosition, endPosition - startPosition)));
                            }
                            else
                            {
                                attachments.Add(new GeneralAttachment(msg.Substring(startPosition, endPosition - startPosition)));
                            }
                            break;
                    }
                    partCount++;
                    startPosition = endPosition;
                }
            }
            while (!gotEndBoundary);
//            persistDuration = (int)(ConnectionManager.getInstance().getPersistDuration(header.SvcIA).TotalSeconds);
        }

        public string getHost() { return receivedHost; }


        public override string getHl7Payload()
        {
            return hl7message.HL7Payload;
        }

        public override string ResolvedUrl
        {
            get { return resolvedUrl; }
        }

        public override void persist()
        {
            if (!persistable)
                return;
            if (hasBeenPersisted)
                return;
            persistenceFile = ConnectionManager.getInstance().getMessageDirectory() + odsCode + "_" + header.MessageId;
            using (FileStream fs = new FileStream(persistenceFile, FileMode.Create, FileAccess.Write))
            {
                try
                {
                    write(fs);
                    hasBeenPersisted = true;
                }
                catch
                {
                    // TODO: Log. persist() is called just before transmission, but it only does anything
                    // if "hasBeenPersisted" is false. If the call to write() throws an exception we end up
                    // here and can log it, but hasBeenPersisted is still false. So there will be another
                    // attempt when it re-tries from memory. If the transmission attempt worked (for
                    // example the persistence failed due to a disk-full condition but the network was OK)
                    // then there won't be another attempt... but by that time the persisted copy of the
                    // message would have been deleted anyway.
                }
            }
        }

        /**
         * Internal call to parse the HTTP header content-type's "boundary" clause to determine the MIME part boundary
         */ 
        private string setMimeBoundary(string ctype)
        {
            StringBuilder mb = new StringBuilder("--");
            int boundary = ctype.IndexOf("boundary");
            if (boundary == -1)
                return "";
            boundary += 8; // "boundary".length
            int startBoundary = 0;
            int endBoundary = 0;
            while(boundary < ctype.Length) {
                if (startBoundary == 0) {
                    if (ctype[boundary] == '=') {
                        boundary++;
                        if (ctype[boundary] == '"') {
                            boundary++;
                        }
                        startBoundary = boundary;
                        boundary++;
                        continue;
                    }
                    throw new Exception("Invalid Content-Type: MIME boundary not properly defined (spaces ?)");
                } else{
                    char c = ctype[boundary];
                    switch (c) {
                        case ';':
                        case '"':
                            endBoundary = boundary;
                            break;
                        case ' ':
                            throw new Exception("Invalid Content-Type: MIME boundary not properly defined (spaces ?)");
                        default:
                            break;

                    }
                }
                if (endBoundary == 0) {
                    boundary++;
                } else {
                    break;
                }
            }
            if (endBoundary == 0) {
                mb.Append(ctype.Substring(startBoundary));
            } else {
                mb.Append(ctype.Substring(startBoundary, endBoundary - startBoundary));
            }
            return mb.ToString();
        }

        /**
         * Internal call to read HTTP headers without a .Net buffered reader messing up the rest of the
         * stream content.
         */ 
        private string readLine(Stream s)
        {
            StringBuilder sb = new StringBuilder();
            int c = -1;
            while ((c = s.ReadByte()) != '\n')
            {
                if (c == -1)
                {
                    break;
                }
                else
                {
                    if (c != '\r')
                        sb.Append((char)c);
                }
            }
            sb.Append((char)c);
            return sb.ToString();
        }
        
        /**
         * Construct an EbXmlMessage for sending.
         * 
         * @param s An SdSTransmissionDetails instance for recipient information
         * @param m SpineHL7Message instance to be sent
         * @param c Reference to an SDSconnection for resolving URLs
         */ 
        public EbXmlMessage(SdsTransmissionDetails s, SpineHL7Message m)
        {
            SDSconnection c = ConnectionManager.getInstance().SdsConnection;
            odsCode = s.Org;
            header = new EbXmlHeader(this, s);
            type = EBXML;
            hl7message = m;
            string svcurl = c.resolveUrl(s.SvcIA);
            if (svcurl == null)
                resolvedUrl = s.Url;
            else
                resolvedUrl = svcurl;
            if (s.Retries != SdsTransmissionDetails.NOT_SET)
            {
                retryCount = s.Retries;
                minRetryInterval = s.RetryInterval;
                persistDuration = s.PersistDuration;
            }
            attachments = new List<Attachment>();
            persistable = (s.Retries > 0);
        }

        public override void setResponse(Sendable r)
        {
            response = (EbXmlMessage)r;
        }

        public override Sendable getResponse()
        {
           return response;
        }

        public override string getMessageId()
        {
            return header.MessageId;
        }

        public override void setMessageId(string id)
        {
            header.MessageId = id;
        }

        public override void write(Stream s)
        {
            StringBuilder sb = new StringBuilder(FIRSTMIMEPREFIX);
            sb.Append(mimeboundary);
            sb.Append(header.makeMimeHeader());
            sb.Append(header.serialise());
            sb.Append(MIMEPREFIX);
            sb.Append(mimeboundary);
            sb.Append(hl7message.makeMimeHeader());
            sb.Append(hl7message.serialise());
            if (attachments != null)
            {
                foreach (Attachment a in attachments)
                {
                    sb.Append(MIMEPREFIX);
                    sb.Append(mimeboundary);
                    sb.Append(a.makeMimeHeader());
                    sb.Append(a.serialise());
                }
            }
            sb.Append(MIMEPREFIX);
            sb.Append(mimeboundary);
            sb.Append(MIMEPOSTFIX);
            long l = sb.Length;
            if (l < MAX_MESSAGE_SIZE)
            {
                string hdr = makeHttpHeader(l);
                StreamWriter t = new StreamWriter(s);
                t.Write(hdr);
                t.Write(sb.ToString());
                t.Flush();
            }
            else
            {
                // NEXT VERSION: Implement this... Bit of an exception case here, may be
                // better to have a caller-settable flag (or a different method) for "send using large
                // message protocol".
                //
                string mainMessage = sendLargeMessage(sb.ToString());                
            }
        }

        private string sendLargeMessage(string s)
        {
            throw new NotImplementedException();
        }

        private string makeHttpHeader(long l)
        {
            StringBuilder sb = new StringBuilder(HTTPHEADER);
            if (resolvedUrl == null)
            {
                // We end up here if we're doing a persisted reliable message. Either it is going to
                // be handled (re-sent) or it is being expired.
                //
                sb.Replace("__CONTEXT_PATH__", receivedContextPath);
                sb.Replace("__HOST__", receivedHost);
            }
            else
            {
                Uri u = new Uri(resolvedUrl);
                sb.Replace("__CONTEXT_PATH__", u.AbsolutePath);
                sb.Replace("__HOST__", u.Host);
            }
            StringBuilder sa = new StringBuilder(header.Service);
            sa.Append("/");
            sa.Append(header.InteractionId);
            soapAction = sa.ToString();
            sb.Replace("__SOAP_ACTION__", soapAction);
            sb.Replace("__CONTENT_LENGTH__", l.ToString());
            sb.Replace("__MIME_BOUNDARY__", mimeboundary);
            sb.Replace("__START_ID__", header.ContentId);
            return sb.ToString();
        }

        public EbXmlHeader Header
        {
            get { return header; }
        }

        public string MimeBoundary
        {
            get { return mimeboundary; }
            set { mimeboundary = value; }
        }

        public SpineHL7Message HL7Message
        {
            get { return hl7message; }
            set { hl7message = value; }
        }
        public List<Attachment> Attachments
        {
            get { return attachments; }
        }
    }
}
