using System;
using System.IO;
using System.Text;
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
     * Class to wrap an ebXml MessageAcknowledgment, as a concrete subclass of Sendable so it can
     * be returned asynchronously.
     * 
     * The ebXml MessageAcknowledgment body is the same whether it is returned synchronously (e.g.
     * for SPine-reliable or "forward reliable" messaging) or asynchronously (for "end-party reliable"
     * messaging). So it is created by the ConnectionManager. Where the acknowledgment is returned
     * synchronously it is just written to the already-open stream. Asynchronous acknowledgements are
     * constructed the same way, but then wrapped in an instance of this class so that they can
     * be passed to the ConnectionManager.send() method.
     */ 
    public class EbXmlAcknowledgment : Sendable
    {
        public const string ACK_HTTP_HEADER = "POST /reliablemessaging/intermediary HTTP/1.1\r\nHost: __HOST__\r\nContent-Length: __CONTENT_LENGTH__\r\nConnection: close\r\nContent-Type: text/xml\r\nSOAPAction: urn:urn:oasis:names:tc:ebxml-msg:service/Acknowledgment\r\n\r\n";
        private string ack = null;
        private string host = null;
        private const string ACKSERVICE = "urn:oasis:names:tc:ebxml-msg:service:Acknowledgment";

        /**
         * Construct the sendable acknowledgement from a string containing the body of the ebXml MessageAcknowledgment.
         */ 
        public EbXmlAcknowledgment(string a)
        {
            ack = a;
            resolvedUrl = Connection.ConnectionManager.getInstance().SdsConnection.resolveUrl(ACKSERVICE);
        }

        public override void persist() {}

        /**
         * Static method for interrogating a received EbXmlAcknowledgment to determine the message
         * id that it is acking.
         * 
         * @param Received acknowledgment body
         * @returns Extracted message id
         */ 
        public static string getAckedMessageId(string msg)
        {
            if (msg == null)
                return null;
            int start = msg.IndexOf("RefToMessageId>");
            if (start == -1)
                return null;
            start += "RefToMessageId>".Length;
            int end = msg.IndexOf("<", start);
            if (end == -1)
                return null;
            return msg.Substring(start, end - start);
        }

        public override string getHl7Payload()
        {
            return "EbXml Acknowledgment - no HL7 payload";
        }

        public override string ResolvedUrl
        {
            get { return resolvedUrl; }
        }

        /**
         * Serialises the acknowledgment to the given stream.
         */ 
        public override void write(Stream s)
        {
            StringBuilder sb = new StringBuilder(ACK_HTTP_HEADER);
            sb.Replace("__HOST__", host);
            sb.Replace("__CONTENT_LENGTH__", ack.Length.ToString());
            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            s.Write(bytes, 0, bytes.Length);
            s.Flush();
            bytes = Encoding.UTF8.GetBytes(ack);
            s.Write(bytes, 0, bytes.Length);            
        }

        public void setHost(string h) { host = h; }

        public override void setResponse(Sendable r) { }
        public override Sendable getResponse() { return null; }

        public override string getMessageId() { return null; }
        public override void setMessageId(string s) { }

    }
}
