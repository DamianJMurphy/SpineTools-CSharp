using System;
/*
Copyright 2014 Health and Social Care Information Centre,
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
     * This class provides a SynchronousResponseHandler that does nothing. It can be
     * used when the caller is simply going to extract the synchronous response from the
     * SpineSOAPRequest after it has been put there by the ConnectionManager.send() method.
     */
    public class NullSynchronousResponseHandler : ISynchronousResponseHandler
    {
        public NullSynchronousResponseHandler() { }

        public void handle(SpineSOAPRequest r) { }
    }
}
