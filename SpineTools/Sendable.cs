using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using SpineTools.Connection;
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
     * Abstract class for "Sendable" Spine message types.
     */ 
    public abstract class Sendable
    {
        public const int UNDEFINED = 0;
        public const int EBXML = 1;
        public const int SOAP = 2;
        public const int ACK = 3;

        protected int type = UNDEFINED;
        protected int retryCount = SdsTransmissionDetails.NOT_SET;
        protected int minRetryInterval = SdsTransmissionDetails.NOT_SET;
        protected int persistDuration = SdsTransmissionDetails.NOT_SET;
        protected string synchronousResponse = null;
        protected string resolvedUrl = null;
        protected string soapAction = null;

        protected string odsCode = "ODS";

        protected DateTime started = DateTime.Now;
        protected DateTime lastTry = DateTime.MinValue;
        protected int tries = 0;

        public void setOdsCode(string o) 
        { 
            if (o != null)
                odsCode = o; 
        }

        public string getOdsCode() { return odsCode; }

        /**
         * Save the Sendable to a file, and call a soapAction-specific IExpiryHandler if one
         * is set. 
         */
        public void Expire()
        {
            ConnectionManager cm = ConnectionManager.getInstance();

            string dir = cm.ExpiredMessagesDirectory;
            if (dir != null)
            {
                try
                {
                    StringBuilder fileName = new StringBuilder(dir);
                    fileName.Append("\\");
                    fileName.Append(getMessageId());
                    fileName.Append(".msg");
                    FileStream fs = new FileStream(fileName.ToString(), FileMode.Create);
                    this.write(fs);
                    fs.Flush();
                    fs.Close();
                }
                catch (Exception e)
                {
                    EventLog logger = new EventLog("Application");
                    logger.Source = ConnectionManager.LOGSOURCE;
                    StringBuilder sb = new StringBuilder("Failed to save expired message ");
                    sb.Append(getMessageId());
                    sb.Append(" : ");
                    sb.Append(e.ToString());
                    logger.WriteEntry(sb.ToString(), EventLogEntryType.Error);
                }
            }
            IExpiredMessageHandler h = cm.getExpiryHandler(soapAction);
            if (h != null)
                h.handleExpiry(this);
        }

        /**
         * The time when transmission attempts on this Sendable were started, used for retryable messages.
         */ 
        public DateTime Started
        {
            get { return started; }
        }

        /**
         * Time transmission was last re-tried.
         */ 
        public DateTime LastTry
        {
            get 
            {
                return lastTry; 
            }
        }

        /**
         * Called from the connection manager to see if it is OK to try to
         * send this. "Yes" if non-TMS-reliable (because we won't have seen it
         * before, otherwise), "yes" if we still have retries to do. A timer 
         * will sweep up persistDuration expiries in a different thread so
         * don't bother checking that here.
         */
        public bool recordTry()
        {
            if (retryCount < 1)
                return true;
            if (++tries > retryCount)
                return false;
            lastTry = DateTime.Now;
            return true;
        }

        /**
         * Message type: Spine SOAP, ebXml or an asynchronous acknowledgment.
         */ 
        public int Type
        {
            get { return type; }
        }

        public string SoapAction
        {
            get { return soapAction; }
        }

        public int RetryCount
        {
            get { return retryCount; }
            set { retryCount = value; }
        }

        public int RetryInterval
        {
            get { return minRetryInterval; }
            set { minRetryInterval = value; }
        }

        public int PersistDuration
        {
            get { return persistDuration; }
            set { persistDuration = value; }
        }

        public string SyncronousResponse
        {
            get { return synchronousResponse; }
            set { synchronousResponse = value; }
        }

        public abstract void write(Stream s);
        public abstract void setResponse(Sendable r);
        public abstract Sendable getResponse();
        public abstract string getMessageId();
        public abstract void setMessageId(string s);
        public abstract string ResolvedUrl { get; }
        public abstract string getHl7Payload();
        public abstract void persist();
    }
}
