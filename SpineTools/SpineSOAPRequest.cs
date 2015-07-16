using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
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
     * Sendable concrete subclass representing a synchronous Spine query. Note that because this
     * type of Spine message is always synchronous, it is built for sending only, and is never
     * constructed from an accepted network stream.
     */ 
    public class SpineSOAPRequest : Sendable
    {
        private const string SOAPREQUESTTEMPLATE = "SpineTools.SpineSoapTemplate.txt";
        private const string HTTPHEADER = "POST __CONTEXT_PATH__ HTTP/1.1\r\nHost: __HOST__\r\nSOAPAction: \"__SOAP_ACTION__\"\r\nContent-Length: __CONTENT_LENGTH__\r\nContent-Type: text/xml; charset=utf-8\r\nConnection: close\r\n\r\n";
        private SpineHL7Message hl7Message = null;
        private SdsTransmissionDetails transmissionDetails = null;
        private string messageid = null;
        private static string template = null;
        private static Exception bootException = null;
        private static string myIp = null;
        private string myAsid = null;

        /**
         * Construct a SpineSOAPRequest for sending.
         * 
         * @param s An SdSTransmissionDetails instance for recipient information
         * @param m SpineHL7Message instance to be sent
         * @param c Reference to an SDSconnection for resolving URLs
         */ 
        public SpineSOAPRequest(SdsTransmissionDetails s, SpineHL7Message m)
        {
            if (bootException != null)
                throw bootException;
            SDSconnection c = ConnectionManager.getInstance().SdsConnection;
            type = Sendable.SOAP;
            myAsid = c.MyAsid;
            messageid = Guid.NewGuid().ToString().ToUpper();
            lock (messageid)
            {
                if (myIp == null)
                {
                    ConnectionManager cm = ConnectionManager.getInstance();
                    if (cm.MyIp == null)
                    {
                        try
                        {
                            IPAddress[] addresses = Dns.GetHostAddresses("");
                            myIp = addresses[0].ToString();
                        }
                        catch (Exception e)
                        {
                            bootException = e;
                            throw e;
                        }
                        if (myIp == null)
                        {
                            bootException = new Exception("No non-localhost IP interfaces found");
                            throw bootException;
                        }
                    }
                    else
                    {
                        myIp = cm.MyIp;
                    }
                }
                if (template == null)
                {
                    Assembly assembly = Assembly.GetExecutingAssembly();
                    StreamReader sr = new StreamReader(assembly.GetManifestResourceStream(SOAPREQUESTTEMPLATE));
                    StringBuilder sb = new StringBuilder();
                    try
                    {
                        String line = null;
                        while ((line = sr.ReadLine()) != null)
                        {
                            sb.Append(line);
                            sb.Append("\r\n");
                        }
                        template = sb.ToString();
                    }
                    catch (Exception e)
                    {
                        bootException = e;
                        throw e;
                    }

                }
            }
            transmissionDetails = s;
            hl7Message = m;
            string svcurl = c.resolveUrl(s.SvcIA);
            if (svcurl == null)
                resolvedUrl = s.Url;
            else
                resolvedUrl = svcurl;
        }

        public override string getHl7Payload()
        {
            return hl7Message.HL7Payload;
        }

        public override string ResolvedUrl
        {
            get { return resolvedUrl; }
        }

        public override void setResponse(Sendable r) {}

        public override Sendable getResponse()
        {
           return null;
        }

        public override void persist() {}

        public override string getMessageId()
        {
            return messageid;
        }

        public override void setMessageId(string id) {}

        public override void write(Stream s)
        {
            // Assemble the Spine synchronous SOAP request and write it to "s". Strip off
            // any XML PI from the top of the serialised SpineHL7Message.
            //
            // 1. Extract the communicationFunctionSnd/Rcv bits of the HL7 message, subsitute them into the template
            // 2. Substitute in the URL, and the addressing "stuff" (that can come from our IP address
            //  of be loaded from the Registry
            // 3. Get the serialised XML of the HL7 and strip off any <?xml...?>, 
            // 4. "makeHttpHeader()" with the content length

            StringBuilder sb = new StringBuilder(template);
            sb.Replace("__MESSAGE_ID__", messageid);
            StringBuilder sa = new StringBuilder(transmissionDetails.Service);
            sa.Append("/");
            sa.Append(transmissionDetails.InteractionId);
            soapAction = sa.ToString();
            sb.Replace("__SOAPACTION__", soapAction);
            sb.Replace("__RESOLVED_URL__", resolvedUrl);
            sb.Replace("__MY_IP__", myIp);
            sb.Replace("__TO_ASID__", transmissionDetails.Asid[0]);
            sb.Replace("__MY_ASID__", myAsid);

            // Body...
            string hl7 = hl7Message.serialise();
            if (hl7.StartsWith("<?xml "))
            {
                sb.Replace("__HL7_BODY__", hl7.Substring(hl7.IndexOf('>') + 1));
            }
            else
            {
                sb.Replace("__HL7_BODY__", hl7);
            }
            long l = sb.Length;

            string hdr = makeHttpHeader(l);
            StreamWriter t = new StreamWriter(s);
            t.Write(hdr);
            t.Write(sb.ToString());
            t.Flush();
        }
        
        private string makeHttpHeader(long l)
        {
            StringBuilder sb = new StringBuilder(HTTPHEADER);
            Uri u = new Uri(resolvedUrl);
            sb.Replace("__CONTEXT_PATH__", u.AbsolutePath);
            sb.Replace("__HOST__", u.Host);
            sb.Replace("__SOAP_ACTION__", soapAction);
            sb.Replace("__CONTENT_LENGTH__", l.ToString());
            return sb.ToString();
        }

    }
}
