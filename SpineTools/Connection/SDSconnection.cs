using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.DirectoryServices.Protocols;
using System.IO;
using System.Security.Cryptography.X509Certificates;
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
     * Class to manage the LDAP-over-TLS connection to SDS, local cacheing, and queries. The SDSconnection
     * also supports a "URL resolver" mechanism based on an external file which works around a serious
     * design error in SDS. That is, endpoint URL attributes sometimes point to the actual endpoint for
     * sending a message, and sometimes don't. And you "just have to know" which ones.
     */ 
    public class SDSconnection
    {
        private const string SDS_CONNECTION_REGSITRY_KEY = "HKEY_LOCAL_MACHINE\\Software\\HSCIC\\SDSConnection";
        private const string SDS_SERVER_REGVAL = "Server";
        private const string SDS_CERT_FILE_REGVAL = "CertificateFile";
        private const string SDS_CERT_PASS_REGVAL = "CertPass";
        private const string SDS_CACHE_FILE_REGVAL = "cacheDirectory";
        private const string SDS_REFRESH_PERIOD_REGVAL = "RefreshPeriod";
        private const string URL_RESOLVER_FILE = "URLResolverFile";
        private const string MY_ASID = "MyAsid";
        private const string MY_PARTY_KEY = "MyPartyKey";

        private const int DEFAULT_REFRESH_PERIOD = 24;

        private const string LOGSOURCE = "SDSConnection";

        private static string[] ALL_ATTRIBUTES = { "*" };
        private static string[] UNIQUE_IDENTIFIER = { "uniqueIdentifier" };
        //private const string SERVICES_ROOT = "ou=services, o=nhs";
        private const string SERVICES_ROOT = "o=nhs";
        private const string MHSQUERY = "(&(objectclass=nhsMHS)(nhsMHSSvcIA=__SERVICE__)(nhsIDcode=__ORG__)__PARTYKEYFILTER__)";
        private const string PKFILTER = "(nhsMhsPartyKey=__PK__)";
        private const string ASQUERY = "(&(objectclass=nhsAS)(nhsASSvcIA=__SERVICE__)(nhsIDcode=__ORG__)(nhsMhsPartyKey=__PK__))";
        private const string PARTYKEY = "nhsmhspartykey";
        private const string UNIQUEIDENTIFIER = "uniqueidentifier";
        private const int SINGLEVALUE = 0;
        // private const int LDAPS_PORT = 636;

        private const int LDAPS_PORT = 389;

        private string server = null;
        private string cacheDir = null;
        private string certificateFile = null;
        private string urlResolverFile = null;
        private int cacheRefreshPeriod = DEFAULT_REFRESH_PERIOD;
        private X509Certificate certificate = null;

        private SDScache cache = null;
        private Dictionary<string, string> urlResolver = null;
        private string myAsid = null;
        private string myPartyKey = null;

        /**
         * Instantiate the SDSconnection, reading configuration details from the Registry. There is an
         * additional dependency on an external PKCS#12 (.pfx) file that contains the Spine endpoint
         * certificate and key.
         */ 
        public SDSconnection()
        {
            server = (string)Registry.GetValue(SDS_CONNECTION_REGSITRY_KEY, SDS_SERVER_REGVAL, "");
            cacheDir = (string)Registry.GetValue(SDS_CONNECTION_REGSITRY_KEY, SDS_CACHE_FILE_REGVAL, "");
            certificateFile = (string)Registry.GetValue(SDS_CONNECTION_REGSITRY_KEY, SDS_CERT_FILE_REGVAL, "");
            urlResolverFile = (string)Registry.GetValue(SDS_CONNECTION_REGSITRY_KEY, URL_RESOLVER_FILE, "");
            myAsid = (string)Registry.GetValue(SDS_CONNECTION_REGSITRY_KEY, MY_ASID, "");
            myPartyKey = (string)Registry.GetValue(SDS_CONNECTION_REGSITRY_KEY, MY_PARTY_KEY, "");
            EbXmlHeader.setMyPartyKey(myPartyKey);

            string s = (string)Registry.GetValue(SDS_CONNECTION_REGSITRY_KEY, SDS_REFRESH_PERIOD_REGVAL, "");
            loadUrlResolver();
            if (s.Length > 0)
            {
                try
                {
                    cacheRefreshPeriod = Int32.Parse(s);
                }
                catch (Exception)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    StringBuilder sb = new StringBuilder("Registry entry ");
                    sb.Append(SDS_REFRESH_PERIOD_REGVAL);
                    sb.Append(" has invalid non-integer value '");
                    sb.Append(s);
                    sb.Append("'. Using default value ");
                    sb.Append(DEFAULT_REFRESH_PERIOD);
                    sb.Append(" hours instead");
                    logger.WriteEntry(sb.ToString(), EventLogEntryType.Warning);
                }
            }
            if (server.Length == 0)
            {
                if (cacheDir.Length == 0)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    logger.WriteEntry("Neither LDAP server name nor cache file registry entries are set - cannot resolve anything!", EventLogEntryType.Error);
                }
                else
                {
                    cache = new SDScache(cacheDir, cacheRefreshPeriod);
                }
            }
            else
            {
                if (cacheDir.Length == 0)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    logger.WriteEntry("Cache file registry entry not set: all queries will be resolved directly from SDS and this MAY impact performance", EventLogEntryType.Warning);
                }
                else
                {
                    cache = new SDScache(cacheDir, cacheRefreshPeriod);
                }
            }
            if (certificateFile.Length > 0)
            {
                try
                {
                    String certPass = (string)Registry.GetValue(SDS_CONNECTION_REGSITRY_KEY, SDS_CERT_PASS_REGVAL, "");
                    certificate = new X509Certificate(certificateFile, certPass);
                }
                catch (Exception e)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = LOGSOURCE;
                    StringBuilder sb = new StringBuilder("Certificate ");
                    sb.Append(certificateFile);
                    sb.Append(" failed to load: ");
                    sb.Append(e.ToString());
                    sb.Append(" - no lookups can be made against SDS");
                    logger.WriteEntry(sb.ToString(), EventLogEntryType.Error);
                }
            }
            else
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                logger.WriteEntry("No certificate provided - queries will be resolved from cache only", EventLogEntryType.Warning);
            }
        }

        public string MyAsid
        {
            get { return myAsid; }
        }

        public string MyPartyKey
        {
            get { return myPartyKey; }
        }

        /**
         * Load the file that maps "SvcIA" values (i.e. interaction ids qualified by service name) to actual
         * service URLs for transmission. This file is environment-specific.
         */ 
        private void loadUrlResolver()  
        {
            if (urlResolverFile.Length == 0)
                return;
            urlResolver = new Dictionary<string, string>();
            using (StreamReader sr = new StreamReader(urlResolverFile))
            {
                String line = null;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.StartsWith("#") || (line.Trim().Length == 0))
                        continue;
                    int tab = line.IndexOf('\t');
                    if (tab != -1)
                    {
                        string s = line.Substring(0, tab);
                        string u = line.Substring(tab + 1);
                        urlResolver.Add(s, u);
                    }
                }

            }
        }

        /**
         * Work around the design flaw in SDS that the nhsMhsEndpoint URL doesn't always contain
         * the URL that a sender needs to use. If there is a "resolved" URL, return it, if not
         * return null to indicate that the message should be sent to the endpoint URL given in
         * the SdsTransmissionDetails.
         * 
         * @param svcia Service-qualified interaction id for the message to be sent.
         * @returns Resolved URL or null if the endpoint URL in SDS should be used.
         */
        public string resolveUrl(string svcia)
        {

            if (urlResolver == null)
                return null;
            if (!urlResolver.ContainsKey(svcia))
                return null;
            return urlResolver[svcia];
        }

        /**
         * Resolves a list of SdsTransmissionDetails objects matching the given parameters. If there
         * is a local cache, this is read first and any match returned from there. Otherwise (or if
         * there is nomatch from the local cache) SDS is queried. Where details are retrieved from SDS
         * they are cached locally before being returned.
         * 
         * @param s "SvcIA" service-qualified interaction id, may not be null
         * @param o ODS code, may not be null
         * @param a ASID used as an additional filter if not null
         * @param p Party key used as an additional filter if not null
         * @returns List<SdsTransmissionDetails> of matching endpoint data
         */ 
        public List<SdsTransmissionDetails> getTransmissionDetails(string s, string o, string a, string p)
        {
            if (s == null)
                throw new ArgumentNullException("SvcIA may not be null");
            if (o == null)
                throw new ArgumentNullException("ODS code may not be null");

            List<SdsTransmissionDetails> l = null;
            if (cache != null)
            {
                l = cache.getSdsTransmissionDetails(s, o, a, p);
            }
            if (l == null)
                return ldapGetTransmissionDetails(s, o, a, p);
            else
                return l;
        }

        /**
         * Called from the public getTransmissionDetails() to search SDS when there are no
         * matching records in the local cache. Anything that is read is cached locally.
         */
        private List<SdsTransmissionDetails> ldapGetTransmissionDetails(string s, string o, string a, string p)
        {
            if (certificate == null)
                return null;

            // Two searches: one on nhsMHS to get all the entries for the service for the given organisation,
            // and then another on nhsAS to get all the AS entries (optionally filtering on ASID). Then build the
            // list of SdsTransmissionDetails, and add them to the cache.

            StringBuilder sbMhs = new StringBuilder(MHSQUERY);

            // replace the tags in MHSQUERY __SERVICE__ and __ORG__
            sbMhs.Replace("__SERVICE__", s);
            sbMhs.Replace("__ORG__", o);
            if (p != null)
            {
                StringBuilder pkf = new StringBuilder(PKFILTER);
                pkf.Replace("__PK__", p);
                sbMhs.Replace("__PARTYKEYFILTER__", pkf.ToString());
            }
            else
            {
                sbMhs.Replace("__PARTYKEYFILTER__", "");
            }

            List<SdsTransmissionDetails> results = null;
            SearchRequest srMhs = new SearchRequest(SERVICES_ROOT, sbMhs.ToString(), SearchScope.Subtree, ALL_ATTRIBUTES);
            
            using (LdapConnection connection = new LdapConnection(new LdapDirectoryIdentifier(server, Convert.ToInt32(LDAPS_PORT), true, false)))
            {
                connection.AuthType = AuthType.Anonymous;
//                connection.SessionOptions.QueryClientCertificate = getCertificate;
//                connection.SessionOptions.VerifyServerCertificate = verifyCertificate;
//                connection.SessionOptions.SecureSocketLayer = true;
                connection.SessionOptions.ProtocolVersion = 3;
                
                connection.SessionOptions.SecureSocketLayer = false;
//                connection.SessionOptions.ProtocolVersion = 3;

                List<Dictionary<string,List<string>>> dr = doSearch(connection, srMhs);
                if (dr == null)
                    return null;

                results = new List<SdsTransmissionDetails>();

                // "dr" at this point contains a list of nhsMHS objects for the requested interaction, ODS code
                // and party key if it was specified. Need to build the transmission details from those, and
                // get the ASIDs.
                //
                foreach (Dictionary<string,List<string>> mhs in dr) {
                    SdsTransmissionDetails sds = new SdsTransmissionDetails(mhs);
                    results.Add(sds);
                    if (a == null)
                    {
                        // ASID filtering... need to retrieve the ASID, note that there is only one partykey attribute
                        //
                        StringBuilder sbAs = new StringBuilder(ASQUERY);
                        sbAs.Replace("__SERVICE__", s);
                        sbAs.Replace("__ORG__", o);

                        sbAs.Replace("__PK__", mhs[PARTYKEY][SINGLEVALUE]);
                        SearchRequest srAs = new SearchRequest(SERVICES_ROOT, sbAs.ToString(), SearchScope.Subtree, UNIQUE_IDENTIFIER);
                        List<Dictionary<string,List<string>>> asResponse = doSearch(connection, srAs);

                        // Error, but it is already reported, just don't try to populate
                        if (asResponse != null)
                        {

                            List<string> asids = new List<string>();

                            foreach (Dictionary<string, List<string>> nhsas in asResponse)
                            {
                                // Get the "uniqueIdentifier" from each nhsAS object (i.e. the ASID)
                                string asid = nhsas[UNIQUEIDENTIFIER][SINGLEVALUE];
                                asids.Add(asid);
                            }
                            sds.Asid = asids;
                        }
                    }
                    else
                    {
                        // FIXME: This assumes that the ASID is valid and correct. If it isn't any Spine request
                        // based on it will fail. Assume for now, and look at how to make better later.
                        if (sds.Asid == null)
                            sds.Asid = new List<string>();                     
                        sds.Asid.Add(a);
                    }
                }
            }
            // Consider putting this in a thread
            if (cache != null)
            {
                foreach (SdsTransmissionDetails sdstd in results)
                {
                    cache.cacheTransmissionDetail(sdstd);
                }
            }
            return results;
        }

        /**
         * Handles actually making an LDAP search on SDS.
         */ 
        private List<Dictionary<string,List<string>>> doSearch(LdapConnection l, SearchRequest sr)
        {
            SearchResponse dr = null;
            try
            {
                dr = (SearchResponse)l.SendRequest(sr);
            }
            catch (Exception e)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder er = new StringBuilder("Query request failed: ");
                er.Append(sr.Filter);
                er.Append(" : ");
                er.Append(e.ToString());
                try
                {
                    logger.WriteEntry(er.ToString(), EventLogEntryType.Error);
                }
                catch (Exception ew)
                {
                    Console.Write(er.ToString());
                }
                return null;
            }
            if (dr.ResultCode != ResultCode.Success)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder er = new StringBuilder("Query failed: ");
                er.Append(sr.Filter);
                er.Append(" Result code: ");
                er.Append(dr.ResultCode);
                logger.WriteEntry(er.ToString(), EventLogEntryType.Error);
                return null;
            }
            if (dr.Entries.Count == 0)
            {
                EventLog logger = new EventLog("Application");
                logger.Source = LOGSOURCE;
                StringBuilder er = new StringBuilder("Query returned no data: ");
                er.Append(sr.Filter);
                logger.WriteEntry(er.ToString(), EventLogEntryType.Warning);
                return null;
            }
            
            // Each "Entry" contains an object with a set of attributes. Add a "dictionary" to the list for
            // each entry. Add a string+list to the dictionary for each attribute, with each value of that
            // attribute going into the list. 
            //
            List<Dictionary<string, List<string>>> results = new List<Dictionary<string, List<string>>>();

            for (int i = 0; i < dr.Entries.Count; i++)
            {
                SearchResultEntry sre = dr.Entries[i];
                Dictionary<string,List<string>> entry = new Dictionary<string,List<string>>();               
                results.Add(entry);
                SearchResultAttributeCollection srac = sre.Attributes;                
                foreach (DictionaryEntry da in srac)
                {
                    List<string> attrVals = new List<string>();
                    DirectoryAttribute attr = (DirectoryAttribute)da.Value;
                    for (int j = 0; j < attr.Count; j++)
                    {
                        attrVals.Add((string)attr[j]);
                    }
                    entry.Add(attr.Name.ToLower(), attrVals);
                }
            }
            return results;
        }

        /**
         * Delegate used in ldapGetTransmissionDetails() for setting up the LDAP-over-TLS connection.
         */ 
        public X509Certificate getCertificate(LdapConnection l, byte[][] cas)
        {
            return certificate;
        }

        /**
         * Delegate used in ldapGetTransmissionDetails() for setting up the LDAP-over-TLS connection.
         */ 
        public bool verifyCertificate(LdapConnection l, X509Certificate c)
        {
            // TODO: Do something sensible with this to check certificate c
            return true;
        }
    }
}
