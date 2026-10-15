using System;
using System.Linq;
using System.Reflection;
using OpenTabletDriver.Plugin;

namespace SPen.OpenTabletDriver
{
    /// <summary>
    /// Reads paths from OTD's <c>AppInfo.Current</c>. It lives in OpenTabletDriver.Desktop, which plugins
    /// can't reference, so it's looked up reflectively. Returns null outside OTD (e.g. in the harness).
    /// </summary>
    public static class OtdAppInfo
    {
        public static string? AppDataDirectory => Get("AppDataDirectory");
        public static string? ConfigurationDirectory => Get("ConfigurationDirectory");

        private static string? Get(string property)
        {
            try
            {
                var appInfoType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("OpenTabletDriver.Desktop.AppInfo", throwOnError: false))
                    .FirstOrDefault(t => t != null);
                var current = appInfoType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                return current?.GetType().GetProperty(property)?.GetValue(current) as string;
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                return null;
            }
        }
    }
}
