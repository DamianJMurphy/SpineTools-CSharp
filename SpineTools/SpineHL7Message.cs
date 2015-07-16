using System;
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
     * Concrete subclass of Attachment for holding the "HL7 payload" of a Spine message, and constructing
     * wrappers. With no payload, this class represents the "ITK Trunk" message which consists of minimal
     * HL7 transmission and control act wrappers only to satisfy TMS' requirements for a message to
     * declare ASID and author details.
     */ 
    public class SpineHL7Message : Attachment
    {
        public const string HL7V3XMLMIMETYPE = "application/xml; charset=UTF-8";
        public const string SCHEMAELEMENT = "<eb:Schema eb:location=\"http://www.nhsia.nhs.uk/schemas/HL7-Message.xsd\" eb:version=\"1.0\"/>\r\n";
        public const string DESCRIPTION = "HL7 payload";
        public const string HL7PAYLOADELEMENT = "<hl7ebxml:Payload style=\"HL7\" encoding=\"XML\" version=\"3.0\"/>\r\n";

        public const string HL7V3NS = "urn:hl7-org:v3";

        private const string HL7WRAPPERTEMPLATE = "SpineTools.hl7_wrapper_template.txt";
        private const string AUTHORTEMPLATE = "SpineTools.hl7_author_template.txt";

        private const string HL7V3DATEFORMAT = "yyyyMMddHHmmss";

        private const string INTERACTIONID = "__INTERACTION_ID__";
        private const string MESSAGEID = "__MESSAGE_ID__";
        private const string CREATIONTIME = "__CREATION_TIME__";
        private const string TOASID = "__TO_ASID__";
        private const string MYASID = "__MY_ASID__";
        private const string SUBJECTSTART = "__SUBJECT_START_TAG__";
        private const string SUBJECTEND = "__SUBJECT_END_TAG__";
        private const string HL7PAYLOAD = "__HL7_PAYLOAD__";
        private const string AUTHORUID = "__AUTHOR_UID__";
        private const string AUTHORURP = "__AUTHOR_URP__";
        private const string AUTHORROLE = "__AUTHOR_ROLE__";
        private const string AUTHORELEMENT = "__AUTHOR_ELEMENT__";

        private string hl7v3Payload = null;
        private string interactionId = null;
        private string messageId = null;
        private bool isQuery = false;

        private string myasid = null;
        private string toasid = null;
        private string authoruid = null;
        private string authorurp = null;
        private string authorrole = null;

        // Populated for received messages
        private string fromAsid = null;

        private static string wrapperTemplate = null;
        private static string authorTemplate = null;
        private static Exception bootException = null;

        private string serialisation = null;

        /**
         * Initialise a SpineHL7Message from a parsed XmlDocument containing the payload.
         * 
         * @param ia Service-qualified interaction id
         * @param h Parsed XmlDocument containing the HL7 payload
         */ 
        public SpineHL7Message(string ia, XmlDocument h)
        {
            init(ia, h.OuterXml);
        }

        /**
         * Initialise a SpineHL7Message from an unparsed string containing the payload.
         * 
         * @param ia Service-qualified interaction id
         * @param h String containing the HL7 payload
         */ 
        public SpineHL7Message(string ia, string h)
        {
            init(ia, h);
        }

        /**
         * Used in message receipt to construct a SpineHL7Message from the given MIME part. This
         * also extracts required details from the received meessage.
         * 
         * @param m MIME part body
         */ 
        public SpineHL7Message(string m)
        {
            serialisation = "\r\n\r\n" + stripMimeHeaders(m);
            XmlDocument hl7msg = parseReceivedXml(m);
            hl7v3Payload = hl7msg.OuterXml;
            XmlNodeList n = hl7msg.GetElementsByTagName("id", HL7V3NS);
            if (n.Count == 0)
                throw new Exception("Malformed HL7v3 - no message id");
            messageId = ((XmlElement)n.Item(0)).GetAttribute("root");

            n = hl7msg.GetElementsByTagName("interactionId", HL7V3NS);
            if (n.Count == 0)
                throw new Exception("Malformed HL7v3 - no interaction id");
            messageId = ((XmlElement)n.Item(0)).GetAttribute("extension");

            fromAsid = getAsid(hl7msg, "communicationFunctionSnd");
            toasid = getAsid(hl7msg, "communicationFunctionRcv");

            // IMPROVEMENT: Add a configuration item to (optionally) check that the "toAsid" is us.
        }

        /**
         * Extracts ASID from received message 
         * 
         * @param h Parsed Xml of HL7 interaction
         * @param c "communicationFunctionSnd" or "communicationFunctionRcv" depending on which ASID is wanted
         */ 
        private string getAsid(XmlDocument h, string c)
        {
            XmlNodeList n = h.GetElementsByTagName(c, HL7V3NS);
            if (n.Count == 0)
                throw new Exception("Malformed HL7v3 - no " + c);
            XmlNodeList d = ((XmlElement)n.Item(0)).GetElementsByTagName("device", HL7V3NS);
            if (n.Count == 0)
                throw new Exception("Malformed HL7v3 - no " + c + " device");
            XmlNodeList a = ((XmlElement)d.Item(0)).GetElementsByTagName("id", HL7V3NS);
            if (n.Count == 0)
                throw new Exception("Malformed HL7v3 - no " + c + " id");
            string asid = ((XmlElement)a.Item(0)).GetAttribute("extension");
            if ((asid == null) || (asid.Trim().Length == 0))
                throw new Exception("Malformed HL7v3 - no " + c + " ASID");
            return asid;
        }

        /**
         * Internal call to load an embedded resource template and return it as a string.
         */ 
        private string loadTemplate(string t)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            StreamReader sr = new StreamReader(assembly.GetManifestResourceStream(t));
            StringBuilder sb = new StringBuilder();
            try
            {
                String line = null;
                while ((line = sr.ReadLine()) != null)
                {
                    sb.Append(line);
                    sb.Append("\r\n");
                }
            }
            catch (Exception e)
            {
                bootException = e;
                throw e;
            }
            return sb.ToString();
        }

        private void init(string ia, string h)
        {
            interactionId = ia;
            hl7v3Payload = h;
            isQuery = ia.StartsWith("Q");
            setMimeType(HL7V3XMLMIMETYPE);
            description = DESCRIPTION;
            schema = SCHEMAELEMENT;
            messageId = Guid.NewGuid().ToString().ToUpper();
            lock (messageId)
            {
                if (wrapperTemplate == null)
                {
                    wrapperTemplate = loadTemplate(HL7WRAPPERTEMPLATE);
                }
                if (authorTemplate == null)
                {
                    authorTemplate = loadTemplate(AUTHORTEMPLATE);
                }
            }
        }

        public string MessageID
        {
            get { return messageId; }
        }

        /**
         * Does this use a "query" element, or a "subject" between the control act wrapper and the payload ?
         * We could probably infer this from the initial letter of the interaction id, but that is a bit
         * too much like muck-and-majic even for HL7.
         */ 
        public bool IsQuery
        {
            get { return isQuery; }
            set { isQuery = value; }
        }

        public string MyAsid
        {
            get { return myasid; }
            set { myasid = value; }
        }

        public string FromAsid
        {
            get { return fromAsid; }
        }

        public string ToAsid
        {
            get { return toasid; }
            set { toasid = value; }
        }

        public string AuthorUid
        {
            get { return authoruid; }
            set { authoruid = value; }
        }

        

        public string AuthorUrp
        {
            get { return authorurp; }
            set { authorurp = value; }
        }

        public string AuthorRole
        {
            get { return authorrole; }
            set { authorrole = value; }
        }

        /**
         * Override abstract getEbxmlReference() in Attachment, for making the EbXml manifest.
         */ 
        public override string getEbxmlReference()
        {
            StringBuilder sb = new StringBuilder(REFERENCE);
            sb.Replace("__CONTENT_SCHEME__", "cid:");
            sb.Replace("__CONTENT_ID__", contentid);
            StringBuilder rc = new StringBuilder();
            rc.Append(SCHEMAELEMENT);
            rc.Append(DESCRIPTIONELEMENT);
            rc.Replace("__DESCRIPTION__", description);
            rc.Append(HL7PAYLOADELEMENT);
            sb.Replace("__REFERENCE_BODY__", rc.ToString());
            return sb.ToString();
        }

        /**
         * Override abstract serialise() in Attachment, for sending the message.
         */ 
        public override string serialise()
        {
            if (serialisation != null)
                return serialisation; 
            StringBuilder sb = new StringBuilder(wrapperTemplate);
            sb.Replace(INTERACTIONID, interactionId);
            sb.Replace(MESSAGEID, messageId);
            sb.Replace(TOASID, toasid);
            sb.Replace(MYASID, myasid);
            sb.Replace(CREATIONTIME, DateTime.Now.ToString(HL7V3DATEFORMAT));
            sb.Replace(AUTHORELEMENT, makeAuthorElement());
            if ((hl7v3Payload == null) || (hl7v3Payload.Length == 0)) 
            {
                sb.Replace(SUBJECTSTART, "");
                sb.Replace(HL7PAYLOAD, "");
                sb.Replace(SUBJECTEND, "");
            }
            else
            {
                if (!isQuery)
                    sb.Replace(SUBJECTSTART, "<subject>");
                else
                    sb.Replace(SUBJECTSTART, "");
                sb.Replace(HL7PAYLOAD, stripHl7Xml());
                if (!isQuery)
                    sb.Replace(SUBJECTEND, "</subject>");
                else
                    sb.Replace(SUBJECTEND, "");
            }
            serialisation = sb.ToString();
            return serialisation;
        }

        /**
         * The interaction is built as a string, so get rid of any unwanted XML processing directives.
         */ 
        private string stripHl7Xml()
        {
            if (hl7v3Payload == null)
                return "";
            if (hl7v3Payload.StartsWith("<?xml "))
            {
                return hl7v3Payload.Substring(hl7v3Payload.IndexOf('>') + 1);
            }
            return hl7v3Payload;
        }

        private string makeAuthorElement()
        {
            if (authoruid == null)
                return "";
            StringBuilder sb = new StringBuilder(authorTemplate);
            sb.Replace(AUTHORUID, authoruid);
            sb.Replace(AUTHORURP, authorurp);
            sb.Replace(AUTHORROLE, authorrole);
            return sb.ToString();
        }

        public string HL7Payload
        {
            get { return hl7v3Payload; }
        }

    }
}
