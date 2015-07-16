using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;
using DistributionEnvelopeTools;
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
     * This is an implementation of the IDistributionEnvelopeHandler interface that saves
     * the extracted DistributionEnvelope from an ITK Trunk message, so a file on disk.
     * 
     * It acts both as an "example" implementation, and as a default handler for the ITKTrunkHandler
     * class, when that has no specific handler defined for the ITK service specified in the
     * DistributionEnvelope.
     */ 
    public class DefaultFileSaveDistributionEnvelopeHandler : IDistributionEnvelopeHandler
    {
        private string spoolDirectory = null;
        private string[] COLONSPLIT = { ":" };
        private static string defaultSpoolDirectory = "c:\\";

        private const string LOGSOURCE = "DefaultFileSaveDistributionEnvelopeHandler";

        private const string DEFAULTDISTRIBUTIONENVELOPEHANDLERREGISTRY = "HKEY_LOCAL_MACHINE\\Software\\HSCIC\\TMSConnectionManager\\BasicFileSaveDistributionEnvelopeHandler";
        private const string SPOOLDIRECTORY = "directory";

        /**
         * Handler initialised with the name of the directory to which envelope files will
         * be written.
         */
        public DefaultFileSaveDistributionEnvelopeHandler()
        {
            string s = (string)Registry.GetValue(DEFAULTDISTRIBUTIONENVELOPEHANDLERREGISTRY, SPOOLDIRECTORY, "");
            spoolDirectory = s;
            if (s.Length == 0)
            {
                spoolDirectory = defaultSpoolDirectory;
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sb = new StringBuilder("No explicit spool directory set in ");
                sb.Append(DEFAULTDISTRIBUTIONENVELOPEHANDLERREGISTRY);
                sb.Append("\\");
                sb.Append(SPOOLDIRECTORY);
                sb.Append(" using default directory ");
                sb.Append(defaultSpoolDirectory);
                sb.Append(" instead");
                logger.WriteEntry(sb.ToString(), EventLogEntryType.Warning);
            }
            else
            {
                spoolDirectory = s;
            }
        }

        public static string DefaultSpoolDirectory
        {
            get { return defaultSpoolDirectory; }
            set { defaultSpoolDirectory = value; }
        }

        /**
         * Implementation of the interface' "handle()" method. Determines the
         * file name and tries to write it. Note that this doesn't return any
         * ITK response because formally such a response requires a router. Providing
         * an ITK router here would put a dependency on the ITK code, in the TMS
         * adapter. An implementation that does do ITK responses will be found in
         * the router package.
         */
        public void handle(DistributionEnvelope d)
        {
            StringBuilder sb = new StringBuilder(spoolDirectory);
            sb.Append("\\");
            sb.Append(getLastServiceURIElement(d.getService()));
            sb.Append("_");
            sb.Append(getFileSafeMessageID(d.getTrackingId()));
            sb.Append(".message");
            string filename = sb.ToString();

            try
            {
                using (FileStream fs = new FileStream(filename, FileMode.Create))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        d.parsePayloads();
                        sw.Write(d.getEnvelope());
                        sw.Flush();
                        sw.Close();
                    }
                }
            }
            catch (Exception e)
            {
                EventLog ev = new EventLog("Application");
                ev.Source = LOGSOURCE;
                StringBuilder sbe = new StringBuilder("Failed to save DistributionEnvelope ");
                sbe.Append(d.getTrackingId());
                sbe.Append(" service ");
                sbe.Append(d.getService());
                sbe.Append(" from ");
                sbe.Append(d.getSender().getUri());
                sbe.Append(". Reason: ");
                sbe.Append(e.ToString());
                ev.WriteEntry(sbe.ToString(), EventLogEntryType.Error);
            }
        }

        /**
         * Strip any colons (e.g. from uuid:something) from a message id to make
         * part of a safe file name.
         */
        private string getFileSafeMessageID(String s)
        {
            if (s == null)
            {
                return s;
            }
            if (!s.Contains(":"))
            {
                return s;
            }
            return s.Substring(s.IndexOf(":"));
        }

        /**
         * Return the last colon-delimited element of a service name, or the
         * service name itself if there are no colons.
         */
        private string getLastServiceURIElement(String s)
        {
            if (s == null)
            {
                return "";
            }
            if (!s.Contains(":"))
            {
                return s;
            }
            string[] e = s.Split(COLONSPLIT, StringSplitOptions.RemoveEmptyEntries);
            return e[e.Length - 1];
        }
    }
}
