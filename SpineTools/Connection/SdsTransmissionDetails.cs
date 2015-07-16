using System;
using System.Collections.Generic;
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
     * Class to contain transmission details for sending a particular message type to a single
     * recipient endpoint. This can be instantiated either from an LDAP search on SDS, or from
     * a local disk cache.
     */ 
    public class SdsTransmissionDetails
    {
        public const int NOT_SET = -1;
        public const int SINGLEVALUE = 0;

        private const string NHSIDCODE = "nhsidcode";
        private const string PARTYKEY = "nhsmhspartykey";
        private const string CPAID = "nhsmhscpaid";
        private const string INTERACTIONID = "nhsmhsin";
        private const string SVCIA = "nhsmhssvcia";
        private const string SVCNAME = "nhsmhssn";
        private const string ACKREQ = "nhsmhsackrequested";
        private const string SYNCREPLY = "nhsmhssyncreplymode";
        private const string SOAPACTOR = "nhsmhsactor";
        private const string DUPELIM = "nhsmhsduplicateelimination";
        private const string FQDN = "nhsmhsfqdn";
        private const string RETRIES = "nhsmhsretries"; 
        private const string RETRYINTERVAL = "nhsmhsretryinterval";
        private const string PERSISTDURATION = "nhsmhspersistduration";
        private const string ENDPOINT = "nhsmhsendpoint";

        private string service = null;
        private string interactionid = null;
        private string svcia = null;
        private string orgcode = null;
        private string soapactor = null;
        private string ackrequested = null;
        private string duplicateelimination = null;
        private string syncreply = null;
        private string cpaid = null;
        private string partykey = null;
        private List<string> asid = null;
        private string url = null;
        private int retries = NOT_SET;
        private int retryinterval = NOT_SET;
        private int persistduration = NOT_SET;

        public SdsTransmissionDetails() { }

        public SdsTransmissionDetails(string org, string svc, string id)
        {
            orgcode = org;
            service = svc;
            interactionid = id;
            svcia = svc + ":" + id;
        }

        public SdsTransmissionDetails(Dictionary<string, List<string>> mhs)
        {
            orgcode = mhs[NHSIDCODE][SINGLEVALUE];
            partykey = mhs[PARTYKEY][SINGLEVALUE];
            cpaid = mhs[CPAID][SINGLEVALUE];
            interactionid = mhs[INTERACTIONID][SINGLEVALUE];
            svcia = mhs[SVCIA][SINGLEVALUE];
            service = mhs[SVCNAME][SINGLEVALUE];
            ackrequested = mhs[ACKREQ][SINGLEVALUE];
            syncreply = mhs[SYNCREPLY][SINGLEVALUE];
            try
            {
                soapactor = mhs[SOAPACTOR][SINGLEVALUE];
                duplicateelimination = mhs[DUPELIM][SINGLEVALUE];
            }
            catch (Exception) 
            { 
                // Ignore - fails for synchronous interactions
            }
            url = mhs[ENDPOINT][SINGLEVALUE];
            try
            {
                retries = Int16.Parse(mhs[RETRIES][SINGLEVALUE]);
                retryinterval = iso8601DurationToSeconds(mhs[RETRYINTERVAL][SINGLEVALUE]);
                persistduration = iso8601DurationToSeconds(mhs[PERSISTDURATION][SINGLEVALUE]);
            }
            catch (Exception)
            { 
                // Ignore - non-retryable nhsMHS entry
            }
        }

        public string Org
        {
            get { return orgcode; }
            set { orgcode = value; }
        }

        public string SvcIA
        {
            get { return svcia; }
            set { svcia = value; }
        }

        public string Service
        {
            get { return service; }
            set { service = value; }
        }

        public string InteractionId
        {
            get { return interactionid; }
            set { interactionid = value; }
        }

        public int RetryInterval
        {
            get { return retryinterval; }
            set { retryinterval = value; }
        }
        public int PersistDuration
        {
            get { return persistduration; }
            set { persistduration = value; }
        }

        public int Retries
        {
            get { return retries; }
            set { retries = value; }
        }

        public string Url
        {
            get { return url; }
            set { url = value; }
        }

        public List<string> Asid
        {
            get { return asid; }
            set { asid = value; }
        }

        public string PartyKey
        {
            get { return partykey; }
            set { partykey = value; }
        }

        public string CPAid
        {
            get { return cpaid; }
            set { cpaid = value; }
        }

        public string SyncReply
        {
            get { return syncreply; }
            set { syncreply = value; }
        }

        public bool IsSynchronous
        {
            get { return (syncreply == null) || (syncreply.ToLower().Trim().Equals("none")); }
        }

        public string DuplicateElimination
        {
            get { return (duplicateelimination == null) ? "" : duplicateelimination; }
            set { duplicateelimination = value; }
        }

        public string AckRequested
        {
            get { return ackrequested; }
            set { ackrequested = value; }
        }

        public string SoapActor
        {
            get { return soapactor; }
            set { soapactor = value; }
        }

        public static int iso8601DurationToSeconds(string d)
        {
            int seconds = 0;
            int multiplier = 1;
            for (int i = d.Length - 1; i > -1; i--)
            {
                if (Char.IsLetter(d[i]))
                {
                    switch (d[i])
                    {
                        case 'S':
                            multiplier = 1;
                            break;
                        case 'M':
                            multiplier = 60;
                            break;
                        case 'H':
                            multiplier = 3600;
                            break;
                        case 'T':
                            return seconds;
                    }
                }
                else
                {
                    seconds += (d[i] - '0') * multiplier;
                    multiplier *= 10;
                }
            }
            return seconds;
        }
    }
}
