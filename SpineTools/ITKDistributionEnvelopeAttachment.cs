using System;
using System.Collections.Generic;
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
     * Specialist concrete subclass of Attachment to handle the attachment from an "ITKTrunk" 
     * Spine message that is the ITK Distribution Envelope being shipped.
     * 
     * This class uses the DistributionEnvelopeTools package for parsing and working with
     * instances of the ITK Distribution Envelope.
     */ 
    public class ITKDistributionEnvelopeAttachment : Attachment
    {
        private const string DEFAULT_DESCRIPTION = "ITK Trunk message";
        private const string MIME_TYPE = "text/xml";

        private DistributionEnvelope distributionEnvelope = null;

        /**
         * Constructs the attachment from a string containing the serialised XML of an intact
         * distribution envelope.
         */ 
        public ITKDistributionEnvelopeAttachment(string a)
        {
            string d = stripMimeHeaders(a);
            DistributionEnvelopeHelper deh = DistributionEnvelopeHelper.getInstance();
            distributionEnvelope = deh.getDistributionEnvelope(d);
        }

        /**
         * Constructs the attachment from an existing DistributionEnvelope instance.
         */ 
        public ITKDistributionEnvelopeAttachment(DistributionEnvelope d)
        {
            description = DEFAULT_DESCRIPTION;
            distributionEnvelope = d;
            mimetype = MIME_TYPE;
        }
        
        public DistributionEnvelope DistributionEnvelope
        {
            get { return distributionEnvelope; }
        }

        public override string getEbxmlReference()
        {
            StringBuilder sb = new StringBuilder(REFERENCE);
            sb.Replace("__CONTENT_SCHEME__", "cid:");
            sb.Replace("__CONTENT_ID__", contentid);
            StringBuilder rc = new StringBuilder();
            rc.Append(DESCRIPTIONELEMENT);
            rc.Replace("__DESCRIPTION__", description);
            sb.Replace("__REFERENCE_BODY__", rc.ToString());
            return sb.ToString();
        }

        public override string serialise()
        {
            return distributionEnvelope.getEnvelope();
        }

    }
}
