using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
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
     * Implementation of the ISpineHandler interface, specifically for the "ITKTrunk" message. This class
     * has a handler that will extract an ITKDistributionEnvelopeAttachment instance, and then call an
     * instance of IDistributionEnvelopeHandler to process it. If no specific implementation of 
     * IDistributionEnvelopeHandler is known for the ITK service specified in the distribution envelope,
     * the DefaultFileSaveDistributionEnvelopeHandler is used.
     */ 
    public class ITKTrunkHandler : ISpineHandler
    {
        private const int ITKATTACHMENT = 0;
        private const string LOGSOURCE = "ITKTrunkHandler";
        private Dictionary<string, IDistributionEnvelopeHandler> handlers = null;

        public ITKTrunkHandler()
        {
            handlers = new Dictionary<string, IDistributionEnvelopeHandler>();
        }

        /**
         * Add an IDistributionEnvelopeHandler implementation, against the given ITK service name.
         */ 
        public void addHandler(string s, IDistributionEnvelopeHandler h)
        {
            handlers.Add(s, h);
        }

        public void handle(Sendable s)
        {
            EbXmlMessage ebxml = (EbXmlMessage)s;
            ITKDistributionEnvelopeAttachment a = (ITKDistributionEnvelopeAttachment)ebxml.Attachments[ITKATTACHMENT];
            DistributionEnvelope d = a.DistributionEnvelope;
            IDistributionEnvelopeHandler h = null;
            try
            {
                h = handlers[d.getService()];
            }
            catch (KeyNotFoundException)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder sb = new StringBuilder("No explicit handler found for ");
                sb.Append(d.getService());
                sb.Append(" using default DefaultFileSaveDistributionEnvelopeHandler instead");
                logger.WriteEntry(sb.ToString(), EventLogEntryType.Warning);
                h = new DefaultFileSaveDistributionEnvelopeHandler();
            }
            h.handle(d);
        }
    }
}
