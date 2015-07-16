using System;
using System.Text;
using System.Text.RegularExpressions;
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
     * Abstract class representing a MIME part of a Spine EbXml message. This handles serialisation
     * of the MIME part, plus support for the EbXmlHeader constructing the Manifest element.
     */ 
    public abstract class Attachment
    {
        // Note: Make "inline" and "external" binary attachment concrete subclasses

        public const string CONTENTIDHEADER = "\r\nContent-Id: <";
        public const string CONTENTTYPEHEADER = ">\r\nContent-Type: ";
        public const string CTEHEADER = "\r\nContent-Transfer-Encoding: 8bit\r\n\r\n";

        protected const string REFERENCE = "<eb:Reference xlink:href=\"__CONTENT_SCHEME____CONTENT_ID__\">\r\n__REFERENCE_BODY__\r\n</eb:Reference>\r\n";
        protected const string DESCRIPTIONELEMENT = "<eb:Description xml:lang=\"en\">__DESCRIPTION__</eb:Description>\r\n";

        protected string mimetype = null;
        protected string contentid = Guid.NewGuid().ToString();
        protected string description = null;
        protected string schema = null;
        protected string headerserialisation = null;

        protected void setMimeType(string m) { mimetype = m; }
        public abstract string getEbxmlReference();
        public abstract string serialise();

        /**
         * Compiled regular expression for extracting the MIME type from the content type field of an attachment's
         * MIME headers. 
         */ 
        private Regex mimeTypeExtractor = new Regex("content-type: ([^\r\n]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /**
         * Retrieve the MIME type of a received attachment.
         * 
         * @param String containing the complete attachment, including MIME part headers
         */ 
        protected string getMimeType(string s)
        {
            // We get the attachment plus MIME headers,
            Match m = mimeTypeExtractor.Match(s);
            if (!m.Success)
                throw new Exception("Invalid attachment - content-type not set");
            Capture c = m.Captures[0];
            mimetype = c.Value;
            return mimetype;
        }

        /**
         * Strips the MIME headers and returns the rest of the string after the "blank line"
         * header delimiter.
         * 
         * @param String containing the complete MIME part.
         */ 
        protected string stripMimeHeaders(string s)
        {
            int bodyStart = s.IndexOf("\r\n\r\n");
            if (bodyStart == -1)
            {
                // This is technically wrong, but we'll try it anyway
                //
                bodyStart = s.IndexOf("\n\n");
                if (bodyStart == -1)
                    throw new Exception("Invalid MIME attachment - no header/body delimiter");
            }
            headerserialisation = s.Substring(0, bodyStart);
            return s.Substring(bodyStart).Trim();
        }

        /**
         * Parse the received string as an Xml document.
         * 
         * @param String containing the body of the received MIME part
         * @returns XmlDocument of the parsed MIME part
         * @throws Exceptions if the parser fails
         */ 
        protected XmlDocument parseReceivedXml(string s)
        {
            XmlDocument doc = new XmlDocument();
            string body = stripMimeHeaders(s);
            doc.LoadXml(body);
            return doc;
        }

        /**
         * For transmission, constructs a MIME part header for the attachment.
         */ 
        public string makeMimeHeader()
        {
            if (headerserialisation != null)
                return headerserialisation;
            StringBuilder sb = new StringBuilder(CONTENTIDHEADER);
            sb.Append(contentid);
            sb.Append(CONTENTTYPEHEADER);
            sb.Append(mimetype);
            sb.Append(CTEHEADER);
            headerserialisation = sb.ToString();
            return headerserialisation;
        }

        public string ContentId
        {
            get { return contentid; }
        }

        public string Description
        {
            get { return description; }
            set { description = value; }
        }

        public string Schema
        {
            get { return schema; }
            set { schema = value; }
        }

        public string MimeType
        {
            get { return mimetype; }
        }
    }
}
