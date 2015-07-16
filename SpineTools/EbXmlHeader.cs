using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;
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
     * Concrete subclass of Attachment to represent the ebXml header of a Spine asynchronous message. This can
     * either be constructed by parsing an inbound message, or from an EbXmlMessage instance plus SDS
     * transmission details for a message to be sent.
     * 
     * For sending messages, the header itself is made from the "ebxmlheadertemplate.txt" included with the
     * package.
     */ 
    public class EbXmlHeader : Attachment
    {
        private const string EBXMLHEADERTEMPLATE = "SpineTools.ebxmlheadertemplate.txt";
        private const string DUPLICATEELIMINATIONELEMENT = "<eb:DuplicateElimination/>";
        private const string ACKREQUESTEDELEMENT = "<eb:AckRequested eb:version=\"2.0\" SOAP:mustUnderstand=\"1\" SOAP:actor=\"__SOAP_ACTOR__\" eb:signed=\"false\"/>";
        private const string SYNCREPLYELEMENT = "<eb:SyncReply eb:version=\"2.0\" SOAP:mustUnderstand=\"1\" SOAP:actor=\"http://schemas.xmlsoap.org/soap/actor/next\"/>";
        private const string EBXMLHEADERMIMETYPE = "text/xml";

        private const string EBXMLNS = "http://www.oasis-open.org/committees/ebxml-msg/schema/msg-header-2_0.xsd";

        public const string ISO8601DATEFORMAT = "yyyy'-'MM'-'dd'T'HH':'mm':'ss";

        private static string ebxmlheadertemplate = null;

        private EbXmlMessage ebxml = null;
        public static string myPartyKey = null;
        private string service = null;
        private string interactionid = null;
        private string svcia = null;
//        private string action = null;
        private string cpaid = null;
        private string messageId = null;
        private string conversationId = null;
        private string soapActor = null;
        private string timeStamp = null;
        private string toPartyKey = null;

        // Populated for received messages
        private string fromPartyKey = null; 
        private bool duplicateElimination = true;
        private bool ackrequested = true;
        private bool syncreply = true;

        private Exception bootException = null;
        private string serialisation = null;

        /**
         * Make an EbXmlHeader from a received message, extracting the various header fields.
         * 
         * @param String containing the complete header MIME part, including the MIME headers 
         */ 
        public EbXmlHeader(string s)
        {
            // Build from received message
            serialisation = "\r\n\r\n" + stripMimeHeaders(s);

            // We get the MIME headers (everything between the delimiting MIME booundary strings) 
            // - so make a call to Attachment to parse them (we don't care about MIME type here),

            XmlDocument ebxmlhdr = parseReceivedXml(s);

            // Extract duplicate elimination, ackrequested, conversation id, message id,
            // syncreply and CPAid.

            XmlNodeList n = ebxmlhdr.GetElementsByTagName("DuplicateElimination", EBXMLNS);
            if (n.Count == 0)
                duplicateElimination = false;
            else
                duplicateElimination = true;
            n = ebxmlhdr.GetElementsByTagName("SyncReply", EBXMLNS);
            if (n.Count == 0)
                syncreply = false;
            else
                syncreply = true;
            n = ebxmlhdr.GetElementsByTagName("AckRequested", EBXMLNS);
            if (n.Count == 0)
                ackrequested = false;
            else
                ackrequested = true;

            n = ebxmlhdr.GetElementsByTagName("CPAId", EBXMLNS);
            if (n.Count == 0)            
                throw new Exception("Malformed ebXML - no CPAId");            
            cpaid = n.Item(0).InnerText;
            
            n = ebxmlhdr.GetElementsByTagName("ConversationId", EBXMLNS);
            if (n.Count == 0)
                throw new Exception("Malformed ebXML - no ConversationId");
            conversationId = n.Item(0).InnerText;

            n = ebxmlhdr.GetElementsByTagName("MessageId", EBXMLNS);
            if (n.Count == 0)
                throw new Exception("Malformed ebXML - no MessageId");
            messageId = n.Item(0).InnerText;

            n = ebxmlhdr.GetElementsByTagName("Timestamp", EBXMLNS);
            if (n.Count == 0)
                throw new Exception("Malformed ebXML - no Timestamp");
            timeStamp = n.Item(0).InnerText;

            n = ebxmlhdr.GetElementsByTagName("From", EBXMLNS);
            if (n.Count == 0)
                throw new Exception("Malformed ebXML - no From PartyId");
            XmlNodeList p = ((XmlElement)n.Item(0)).GetElementsByTagName("PartyId", EBXMLNS);
            if (p.Count == 0)
                throw new Exception("Malformed ebXML - From element contains no PartyId");
            fromPartyKey = p.Item(0).InnerText;
            n = ebxmlhdr.GetElementsByTagName("To", EBXMLNS);
            if (n.Count == 0)
                throw new Exception("Malformed ebXML - no To PartyId");
            p = ((XmlElement)n.Item(0)).GetElementsByTagName("PartyId", EBXMLNS);
            if (p.Count == 0)
                throw new Exception("Malformed ebXML - To element contains no PartyId");
            toPartyKey = p.Item(0).InnerText;

            n = ebxmlhdr.GetElementsByTagName("Service", EBXMLNS);
            if (n.Count == 0)
                throw new Exception("Malformed ebXML - no Service");
            service = n.Item(0).InnerText;
            n = ebxmlhdr.GetElementsByTagName("Action", EBXMLNS);
            if (n.Count == 0)
                throw new Exception("Malformed ebXML - no Action");
            interactionid = n.Item(0).InnerText;
            StringBuilder sb = new StringBuilder(service);
            sb.Append(":");
            sb.Append(interactionid);
            svcia = sb.ToString();
          
            setMimeType(EBXMLHEADERMIMETYPE);
        }

        /**
         * Construct an EbXmlHeader for the given EbXmlMessage, using the transmission details.
         * 
         * @param msg The EbXmlMessage for which this will be the header
         * @param s An SdsTransmissionDetails instance containing information on sending this message type to the recipient
         */ 
        public EbXmlHeader(EbXmlMessage msg, SdsTransmissionDetails s)
        {
            if (bootException != null)
                throw bootException;
            ebxml = msg;
            messageId = Guid.NewGuid().ToString().ToUpper();
            setMimeType(EBXMLHEADERMIMETYPE);
            lock (messageId)
            {
                if (ebxmlheadertemplate == null)
                {
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    StreamReader sr = new StreamReader(assembly.GetManifestResourceStream(EBXMLHEADERTEMPLATE));
                    StringBuilder sb = new StringBuilder();
                    try
                    {
                        String line = null;
                        while ((line = sr.ReadLine()) != null)
                        {
                            sb.Append(line);
                            sb.Append("\r\n");
                        }
                        ebxmlheadertemplate = sb.ToString();
                    }
                    catch (Exception e)
                    {
                        bootException = e;
                        throw e;
                    }
                }
            }
            service = s.Service;
            interactionid = s.InteractionId;
            cpaid = s.CPAid;
            soapActor = s.SoapActor;
            toPartyKey = s.PartyKey;
            duplicateElimination = s.DuplicateElimination.Equals("always");
            ackrequested = s.AckRequested.Equals("always");
            syncreply = s.SyncReply.Equals("MSHSignalsOnly");
        }

        internal static void setMyPartyKey(string s)
        {
            myPartyKey = s;
        }

        public override string getEbxmlReference()
        {
            return "";
        }

        public bool SyncReply
        {
            get { return syncreply; }
        }

        public string SvcIA
        {
            get { return svcia; }
        }

        public string Timestamp
        {
            get { return timeStamp; }
            set { timeStamp = value; }
        }

        public string SoapActor
        {
            get { return soapActor; }
            set { soapActor = value; }
        }

        public string ToPartyKey
        {
            get { return toPartyKey; }
            set { toPartyKey = value; }
        }

        public string FromPartyKey
        {
            get { return fromPartyKey; }
            set { fromPartyKey = value; }
        }

        public string ConversationId
        {
            get { return conversationId; }
            set { conversationId = value; }
        }

        public bool DuplicateElimination
        {
            get { return duplicateElimination; }
        }

        public string Service 
        {
            get { return service; }
            set { service = value; }
        }

        public string InteractionId
        {
            get { return interactionid; }
            set { interactionid = value; }
        }

        public string MessageId
        {
            get { return messageId; }
            set { messageId = value; }
        }

        public string CpaId
        {
            get { return cpaid; }
        }

        /**
         * Implementation of Attachment.serialise()
         */ 
        public override string serialise()
        {
            if (serialisation != null)
                return serialisation;
            StringBuilder sb = new StringBuilder(ebxmlheadertemplate);
            sb.Replace("__FROM_PARTY_KEY__", myPartyKey);
            sb.Replace("__TO_PARTY_KEY__", toPartyKey);
            sb.Replace("__CPAID__", cpaid);
            sb.Replace("__CONVERSATION_ID__", (conversationId == null) ? messageId : conversationId);
            sb.Replace("__SERVICE__", service);
            sb.Replace("__INTERACTION_ID__", interactionid);
            sb.Replace("__MESSAGE_ID__", messageId);
            timeStamp = DateTime.Now.ToString(ISO8601DATEFORMAT);
            sb.Replace("__TIMESTAMP__", timeStamp);
            if (duplicateElimination)
                sb.Replace("__DUPLICATE_ELIMINATION__", DUPLICATEELIMINATIONELEMENT);
            else
                sb.Replace("__DUPLICATE_ELIMINATION__", "");
            if (ackrequested)
            {
                StringBuilder ar = new StringBuilder(ACKREQUESTEDELEMENT);
                ar.Replace("__SOAP_ACTOR__", soapActor);
                sb.Replace("__ACK_REQUESTED__", ar.ToString());
            }
            else
            {
                sb.Replace("__ACK_REQUESTED__", "");
            }
            if (syncreply)
                sb.Replace("__SYNC_REPLY__", SYNCREPLYELEMENT);
            else
                sb.Replace("__SYNC_REPLY__", "");
            sb.Replace("__REFERENCES__", buildReferences());
            serialisation = sb.ToString();
            return serialisation;
        }

        /**
         * Internal call to construct the references in the header manifest, from the other attachments
         * held in the EbXmlMessage
         */ 
        private string buildReferences()
        {
            StringBuilder sb = new StringBuilder(ebxml.HL7Message.getEbxmlReference());
            if (ebxml.Attachments != null)
            {
                foreach (Attachment a in ebxml.Attachments) {
                    sb.Append(a.getEbxmlReference());
                }
            }
            return sb.ToString();
        }

    }

}
