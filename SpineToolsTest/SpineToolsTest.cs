using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpineTools;
using SpineTools.Connection;
using DistributionEnvelopeTools;

namespace SpineToolsTest
{
    class SpineToolsTest
    {
        static void Main(string[] args)
        {           
            ConnectionManager cm = ConnectionManager.getInstance();
            SDSconnection c = cm.SdsConnection;
            cm.listen();
           // cm.loadPersistedMessages();

//           List<SdsTransmissionDetails> sdsdetails = c.getTransmissionDetails("urn:nhs:names:services:pdsquery:QUPA_IN000008UK02", "YES", null, null);
// /            List<SdsTransmissionDetails> sdsdetails = c.getTransmissionDetails("urn:nhs:names:services:pdsquery:QUPA_IN020000UK31", "YEA", "631955299542", null);
//            List<SdsTransmissionDetails> sdsdetails = c.getTransmissionDetails("urn:nhs:names:services:mm:PORX_IN020101UK31", "YEA", "631955299542", null);
  /*          string qstring = null;
            using (StreamReader rdr = File.OpenText(@"c:\test\data\query.txt"))
            {
                qstring = rdr.ReadToEnd();
            }
            // SpineHL7Message msg = new SpineHL7Message("QUPA_IN020000UK31", qstring);
            //SpineHL7Message msg = new SpineHL7Message("PORX_IN020101UK31", qstring);
            SpineHL7Message msg = new SpineHL7Message("QUPA_IN000008UK02", qstring);
            SpineSOAPRequest req = new SpineSOAPRequest(sdsdetails[0], msg);
            msg.ToAsid = sdsdetails[0].Asid[0];
            msg.MyAsid = c.MyAsid;
            msg.IsQuery = true;
            //EbXmlMessage eb = new EbXmlMessage(sdsdetails[0], msg, c);

            MemoryStream ms = new MemoryStream();
             req.write(ms);
            //eb.write(ms);
            ms.Seek(0, SeekOrigin.Begin);
            StreamReader sr = new StreamReader(ms);
            string s = sr.ReadToEnd();
            
            cm.send(req, sdsdetails[0]);
            */
            //cm.listen();
//            while (true) ;

//            List<SdsTransmissionDetails> sdsdetails = c.getTransmissionDetails("urn:nhs:names:services:pdsquery:QUPA_IN000008UK02", "YES", null, null);
//            List<SdsTransmissionDetails> sdsdetails = c.getTransmissionDetails("urn:nhs:names:services:pdsquery:QUPA_IN040000UK32", "YES", null, null);
//            List<SdsTransmissionDetails> sdsdetails = c.getTransmissionDetails("urn:nhs:names:services:mm:PORX_IN132004UK30", "YES", null, null);
            List<SdsTransmissionDetails> sdsdetails = c.getTransmissionDetails("urn:nhs:names:services:psis:REPC_IN150015UK05", "YES", null, null);

//            List<SdsTransmissionDetails> sdsdetails = c.getTransmissionDetails("urn:nhs:names:services:pdsquery:QUPA_IN000006UK02", "YES", null, null);
            int i = sdsdetails.Count;
            cm.listen();
            string qstring = null;
            using (StreamReader rdr = File.OpenText(@"c:\test\data\REPC_IN150015UK05_mhstest.xml"))
            {
                qstring = rdr.ReadToEnd();
            }
            SpineHL7Message msg = new SpineHL7Message("REPC_IN150015UK05", qstring);
            msg.ToAsid = sdsdetails[0].Asid[0];
            msg.MyAsid = c.MyAsid;
            msg.AuthorRole = "S0080:G0450:R5080";
            msg.AuthorUid = "687227875014";
            msg.AuthorUrp = "012345678901";

            EbXmlMessage eb = new EbXmlMessage(sdsdetails[0], msg);
    //          eb.Attachments.Add(deattachment);
                        MemoryStream ms = new MemoryStream();
                        eb.write(ms);
                        ms.Seek(0, SeekOrigin.Begin);
                        StreamReader sr = new StreamReader(ms);
                        string s = sr.ReadToEnd();
                   cm.send(eb, sdsdetails[0]);            
        
        }
    }
}
