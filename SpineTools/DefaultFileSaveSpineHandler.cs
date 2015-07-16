using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
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
namespace SpineTools
{
    /**
     * This is an implementation of the ISpineHandler interface to act partly as an example,
     * and partly as a default for non-ITKTrunk messages where there is no specific handler
     * registered in the ConnectionManager, for the message type. ITKTrunk messages are habndled
     * by the ITKTrunkHandler class.
     * 
     * The handle() method of this class just writes the received message to a file on disk. The
     * directory for the received messages is configured via a setting in the Registry.
     */ 
    public class DefaultFileSaveSpineHandler : ISpineHandler
    {
        private string spoolDirectory = null;
        private static string defaultSpoolDirectory = "c:\\";

        private const string LOGSOURCE = "DefaultFileSpineHandler";

        private const string DEFAULTSPINEHANDLERREGISTRY = "HKEY_LOCAL_MACHINE\\Software\\HSCIC\\TMSConnectionManager\\DefaultFileSaveSpineHandler";
        private const string SPOOLDIRECTORY = "directory";

        public DefaultFileSaveSpineHandler()
        {
            string s = (string)Registry.GetValue(DEFAULTSPINEHANDLERREGISTRY, SPOOLDIRECTORY, "");
            spoolDirectory = s;
            if (s.Length == 0)
            {
                spoolDirectory = defaultSpoolDirectory;
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sb = new StringBuilder("No explicit spool directory set in ");
                sb.Append(DEFAULTSPINEHANDLERREGISTRY);
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

        /**
         *  Determine a file name from the interaction id and the message id, and write the received message to it.
         */ 
        public void handle(Sendable s)
        {
            StringBuilder sb = new StringBuilder(spoolDirectory);
            sb.Append("\\");
            sb.Append(getLastServiceURIElement(s.SoapAction));
            sb.Append("_");
            sb.Append(getFileSafeMessageID(s.getMessageId()));
            sb.Append(".message");
            string filename = sb.ToString();

            try
            {
                using (FileStream fs = new FileStream(filename, FileMode.Create))
                {
                    using (StreamWriter sw = new StreamWriter(fs))
                    {
                        sw.Write(s.getHl7Payload());
                        sw.Flush();
                        sw.Close();
                    }
                }
            }
            catch (Exception e)
            {
                EventLog ev = new EventLog("Application");
                ev.Source = LOGSOURCE;
                StringBuilder sbe = new StringBuilder("Failed to save response ");
                sbe.Append(s.getMessageId());
                sbe.Append(" service ");
                sbe.Append(s.SoapAction);
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
            return s.Substring(s.IndexOf('/') + 1);
        }


    }
}
