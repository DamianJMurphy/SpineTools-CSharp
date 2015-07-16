using System;
using Microsoft.Win32;

namespace SpineTools.Connection
{
    public class DefaultRegistryReadPasswordProvider : IPasswordProvider
    {
        public DefaultRegistryReadPasswordProvider() {}

        public string getPassword()
        {
            return (string)Registry.GetValue(ConnectionManager.CONNECTION_MANAGER_REGSITRY_KEY, ConnectionManager.CERT_PASS_REGVAL, "");
        }
    }
}
