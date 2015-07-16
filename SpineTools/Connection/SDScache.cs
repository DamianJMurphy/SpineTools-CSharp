using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
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
     * Class to manage the cache of SDS transmission data, held as JSON files on the local disk.
     */ 
    public class SDScache
    {
        private string cacheDir = null;
        private int refresh = 0;

        // Keyed on service+interaction
        //
        private Dictionary<string, List<SdsTransmissionDetails>> transmission = null;

        /**
         * Instantiate the cache.
         * 
         * @param d Directory on disk for the files
         * @param r Cache refresh period in hours, or zero for no automatic refresh
         */ 
        internal SDScache(string d, int r)
        {
            cacheDir = d;
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
            // TODO: Automatic refresh isn't implemented yet - put it in (timer) if refresh > 0
            refresh = r;
            transmission = new Dictionary<string, List<SdsTransmissionDetails>>();
            load();
        }

        internal void cacheTransmissionDetail(SdsTransmissionDetails sds)
        {
            if (transmission.ContainsKey(sds.SvcIA))
            {
                List<SdsTransmissionDetails> l = transmission[sds.SvcIA];
                bool replaced = false;
                // FIXME: This is *probably* broken, but silently because the s.Asid.Equals will always fail
                // as the ASID list in "s" is never the same physical thing as the one in "sds". If it ever
                // did match we'd get some sort of concurrency fail updating "l" when we're still reading it
                // in the foreach loop. Look at the Java code instead.
                foreach (SdsTransmissionDetails s in l)
                {
                    if (s.Asid.Equals(sds.Asid))
                    {
                        l.Remove(s);
                        l.Add(sds);
                        replaced = true;
                        break;
                    }
                }
                if (!replaced)
                    l.Add(sds);
            }
            else
            {
                List<SdsTransmissionDetails> l = new List<SdsTransmissionDetails>();
                l.Add(sds);
                transmission.Add(sds.SvcIA, l);
            }
                         
            // Write to cache
            // 1. See if we have a directory entry for the SvcIA
            // 2. Create one if necessary
            // 3. Make a file (StreamWriter) for the asid
            // 4. Serialise "sds" to JSON

            IEnumerable<string> svcs = Directory.EnumerateDirectories(cacheDir);
            bool exists = false;

            // FIXME: BUG: This will always fail, since the SvcIA has colons and the file names have equals
            // signs (noticed during porting)
            foreach (string s in svcs)
            {
                if (s.Contains(sds.SvcIA))
                {
                    exists = true;
                    break;
                }                    
            }
            StringBuilder svciadir = new StringBuilder(cacheDir);
            svciadir.Append("\\");

            svciadir.Append(sds.SvcIA);
            svciadir.Replace(":", "=");

            // Put the colon back after the drive letter, duh!
            svciadir.Replace('=', ':', 0, 2);
            if (!exists)
                Directory.CreateDirectory(svciadir.ToString());
            svciadir.Append("\\");
            svciadir.Append(sds.PartyKey);
            string json = JsonConvert.SerializeObject(sds);
            using (StreamWriter sw = new StreamWriter(svciadir.ToString()))
            {
                sw.Write(json);
            }
        }

        /**
         *  Retrieve a list of SdsTransmissionDetails matching the given parameters
         *  
         * @param svcint "SvcIA" value consisting of TMS service name and HL7 interaction id
         * @param ods ODS code of MHS owner
         * @param asid ASID of end system
         * @param pk EbXml PartyId (party key) of the recipient MHS
         */ 
        internal List<SdsTransmissionDetails> getSdsTransmissionDetails(string svcint, string ods, string asid, string pk)
        {
            if (!transmission.ContainsKey(svcint))
                return null;

            List<SdsTransmissionDetails> cachedDetails = transmission[svcint];
            List<SdsTransmissionDetails> output = new List<SdsTransmissionDetails>();

            foreach (SdsTransmissionDetails s in cachedDetails)
            {
                if (s.SvcIA.Equals(svcint))
                {
                    if (ods != null)
                    {
                        if (s.Org.Equals(ods))
                        {
                            if (asid != null)
                            {
                                if (s.Asid.Equals(asid))
                                    output.Add(s);
                            }
                            else
                            {
                                // PK check (only if ASID is null because ASID includes and is more specific than PK)
                                if ((pk == null) || (s.PartyKey.Equals(pk)))
                                    output.Add(s);
                            }
                        }
                    }
                    else
                    {
                        if (asid != null)
                        {
                            if (s.Asid.Equals(asid))
                                output.Add(s);
                        }
                        else
                        {
                            // PK check (only if ASID is null because ASID includes and is more specific than PK)
                            if ((pk == null) || (s.PartyKey.Equals(pk)))
                                output.Add(s);
                        }
                    }
                }
            }
            if (!(output.Count == 0))
                return output;
            else
                return null;
        }

        /**
         * Internal initialisation - load cache
         */ 
        private void load()
        {
            IEnumerable<string> interactions = Directory.EnumerateDirectories(cacheDir);
            foreach (string svc in interactions)
            {
                loadInteraction(svc);
            }
        }

        /**
         * Called by load()
         */ 
        private void loadInteraction(string d)
        {
            string svcinteraction = d.Substring(d.LastIndexOf("\\") + 1);
            StringBuilder sbi = new StringBuilder(svcinteraction);
            sbi.Replace("=", ":");
            svcinteraction = sbi.ToString();
            IEnumerable<string> files = Directory.EnumerateFiles(d);
            List<SdsTransmissionDetails> tx = new List<SdsTransmissionDetails>();
            transmission.Add(svcinteraction, tx);
            foreach (string f in files)
            {
                string sdstransmission = null;
                using (StreamReader tr = new StreamReader(f))
                {
                    sdstransmission = tr.ReadToEnd();
                }
                SdsTransmissionDetails td = JsonConvert.DeserializeObject<SdsTransmissionDetails>(sdstransmission);
                tx.Add(td);
            }
        }
    }
}
