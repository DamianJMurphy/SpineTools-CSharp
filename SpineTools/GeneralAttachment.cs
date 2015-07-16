using System;
using System.Collections.Generic;
using System.Linq;
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
     * Note that the current "implementation" of this class is a placeholder.
     * 
     * Class to represent a "general" binary attachment to a Spine EbXmlMessage
     */ 
    public class GeneralAttachment : Attachment
    {
        private string body = null;

        public GeneralAttachment(string m)
        {
            // For building from a received message
            string type = getMimeType(m);
            body = stripMimeHeaders(m);
        }

        public override string getEbxmlReference()
        {
            // TODO: Implement
            return "";
        }

        public override string serialise()
        {
            // TODO: Implement
            return "";
        }

    }
}
