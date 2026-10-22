using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace PclRegionBypass
{
    /// <summary>
    ///     PCL-CE 地区判定逻辑的忠实移植版（对应 PCL.Core/App/Region/RegionPolicy.cs）。
    ///     仅用于本工具的 --inspect 诊断与自动化测试，不参与实际的补丁流程。
    /// </summary>
    internal enum RegionClassification
    {
        Unknown = 0,
        Other = 1,
        Mainland = 2
    }

    internal sealed class RegionSignals
    {
        public string GeoName;
        public bool GeoNameAvailable;
        public IList<string> PreferredUiLanguages = new List<string>();
        public bool PreferredUiLanguagesAvailable;
        public string UserLocaleName;
        public bool UserLocaleAvailable;
        public string TimeZoneId;
        public bool TimeZoneAvailable;
        public int? DeviceRegionGeoId;
        public bool DeviceRegionAvailable;
    }

    internal sealed class RegionAssessment
    {
        public RegionClassification Classification;
        public string Reason;

        public RegionAssessment(RegionClassification classification, string reason)
        {
            Classification = classification;
            Reason = reason;
        }

        public bool IsRestricted
        {
            get { return Classification == RegionClassification.Mainland || Classification == RegionClassification.Unknown; }
        }

        public string ClassificationText
        {
            get
            {
                switch (Classification)
                {
                    case RegionClassification.Mainland:
                        return "Mainland(中国大陆)";
                    case RegionClassification.Other:
                        return "Other(其它地区)";
                    default:
                        return "Unknown(未知)";
                }
            }
        }
    }

    internal static class RegionPolicyPort
    {
        internal const int MainlandChinaGeoId = 45;
        private const string DeviceRegionRegistryPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\DeviceRegion";
        private const string DeviceRegionValueName = "DeviceRegion";

        private static readonly HashSet<string> ChinaTimeZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "China Standard Time",
            "Asia/Shanghai",
            "PRC"
        };

        public static RegionAssessment Evaluate(RegionSignals signals)
        {
            if (signals == null) throw new ArgumentNullException("signals");

            if (signals.GeoNameAvailable && IsChinaGeoName(signals.GeoName))
                return new RegionAssessment(RegionClassification.Mainland, "geo-cn");

            if (signals.DeviceRegionAvailable && IsChinaDeviceRegion(signals.DeviceRegionGeoId))
                return new RegionAssessment(RegionClassification.Mainland, "device-region-cn");

            bool chinaTimeZone = signals.TimeZoneAvailable && signals.TimeZoneId != null &&
                                 ChinaTimeZones.Contains(signals.TimeZoneId);
            bool hasChinaLocale = signals.UserLocaleAvailable && IsChinaLocale(signals.UserLocaleName);
            bool hasChinaPreferredLanguage = signals.PreferredUiLanguagesAvailable &&
                                             signals.PreferredUiLanguages.Count > 0 &&
                                             IsChinaLocale(signals.PreferredUiLanguages[0]);

            if (chinaTimeZone && (hasChinaLocale || hasChinaPreferredLanguage))
                return new RegionAssessment(RegionClassification.Mainland, "china-time-zone-and-locale");

            if (signals.GeoNameAvailable && IsValidCountryCode(signals.GeoName) && !IsChinaGeoName(signals.GeoName))
                return new RegionAssessment(RegionClassification.Other, "geo-other");

            bool hasExplicitOtherLocale = signals.UserLocaleAvailable && IsExplicitOtherLocale(signals.UserLocaleName) ||
                                          signals.PreferredUiLanguagesAvailable &&
                                          signals.PreferredUiLanguages.Count > 0 &&
                                          IsExplicitOtherLocale(signals.PreferredUiLanguages[0]);
            if (!signals.GeoNameAvailable && signals.TimeZoneAvailable && !chinaTimeZone && hasExplicitOtherLocale)
                return new RegionAssessment(RegionClassification.Other, "other-time-zone-and-locale");

            if (!signals.GeoNameAvailable && !signals.PreferredUiLanguagesAvailable &&
                !signals.UserLocaleAvailable && !signals.TimeZoneAvailable)
                return new RegionAssessment(RegionClassification.Unknown, "signals-unavailable");

            return new RegionAssessment(RegionClassification.Unknown, "insufficient-signals");
        }

        public static RegionSignals ReadWindowsSignals()
        {
            var signals = new RegionSignals();

            bool available;
            signals.GeoName = TryReadGeoName(out available);
            signals.GeoNameAvailable = available;

            signals.PreferredUiLanguages = TryReadPreferredUiLanguages(out available);
            signals.PreferredUiLanguagesAvailable = available;

            signals.UserLocaleName = TryReadUserLocale(out available);
            signals.UserLocaleAvailable = available;

            signals.DeviceRegionGeoId = TryReadDeviceRegion(out available);
            signals.DeviceRegionAvailable = available;

            try
            {
                signals.TimeZoneId = TimeZoneInfo.Local.Id;
                signals.TimeZoneAvailable = !string.IsNullOrEmpty(signals.TimeZoneId);
            }
            catch
            {
                signals.TimeZoneAvailable = false;
            }

            return signals;
        }

        private static bool IsChinaGeoName(string name)
        {
            return string.Equals(name, "CN", StringComparison.OrdinalIgnoreCase) || name == "156";
        }

        private static bool IsChinaDeviceRegion(int? geoId)
        {
            return geoId == MainlandChinaGeoId;
        }

        private static bool IsValidCountryCode(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length != 2) return false;
            foreach (char c in name)
                if (!char.IsLetter(c) || c > 127) return false;
            try
            {
                RegionInfo ignored = new RegionInfo(name);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool IsChinaLocale(string localeName)
        {
            if (string.IsNullOrEmpty(localeName)) return false;
            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(localeName.Replace('_', '-'));
                return string.Equals(culture.Name, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
                       culture.Name.EndsWith("-CN", StringComparison.OrdinalIgnoreCase);
            }
            catch (CultureNotFoundException)
            {
                return false;
            }
        }

        private static bool IsExplicitOtherLocale(string localeName)
        {
            if (string.IsNullOrEmpty(localeName)) return false;
            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(localeName.Replace('_', '-'));
                return !culture.IsNeutralCulture &&
                       !string.Equals(new RegionInfo(culture.Name).TwoLetterISORegionName, "CN",
                           StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                if (ex is CultureNotFoundException || ex is ArgumentException) return false;
                throw;
            }
        }

        private static string TryReadGeoName(out bool available)
        {
            available = false;
            var buffer = Marshal.AllocHGlobal(16 * sizeof(char));
            try
            {
                int length = GetUserDefaultGeoName(buffer, 16);
                if (length > 1 && length <= 16)
                {
                    available = true;
                    return PtrToString(buffer, length - 1);
                }
            }
            catch
            {
                // 落到下面的旧接口
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return TryReadLegacyGeoName(out available);
        }

        private static string TryReadLegacyGeoName(out bool available)
        {
            available = false;
            try
            {
                int geoId = GetUserGeoID(16); // GEOCLASS_NATION
                if (geoId <= 0) return null;
                var buffer = Marshal.AllocHGlobal(16 * sizeof(char));
                try
                {
                    int length = GetGeoInfoW(geoId, 4, buffer, 16, 0); // GEO_ISO2
                    if (length <= 1 || length > 16) return null;
                    available = true;
                    return PtrToString(buffer, length - 1);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return null;
            }
        }

        private static IList<string> TryReadPreferredUiLanguages(out bool available)
        {
            available = false;
            var result = new List<string>();
            try
            {
                uint count = 0;
                uint chars = 0;
                GetUserPreferredUILanguages(8, ref count, IntPtr.Zero, ref chars); // MUI_LANGUAGE_NAME
                if (chars == 0 || chars > 32768) return result;

                IntPtr buffer = Marshal.AllocHGlobal((int)chars * sizeof(char));
                try
                {
                    if (GetUserPreferredUILanguages(8, ref count, buffer, ref chars) == 0) return result;
                    string raw = PtrToString(buffer, (int)chars);
                    available = true;
                    foreach (string item in raw.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
                        result.Add(item);
                    return result;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return result;
            }
        }

        private static string TryReadUserLocale(out bool available)
        {
            available = false;
            var buffer = Marshal.AllocHGlobal(85 * sizeof(char));
            try
            {
                int length = GetUserDefaultLocaleName(buffer, 85);
                if (length <= 1 || length > 85) return null;
                available = true;
                return PtrToString(buffer, length - 1);
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static int? TryReadDeviceRegion(out bool available)
        {
            available = false;
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey key = baseKey.OpenSubKey(DeviceRegionRegistryPath))
                    {
                        if (key == null) continue;
                        object raw = key.GetValue(DeviceRegionValueName);
                        if (raw == null) continue;
                        available = true;
                        return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    // 与 PCL 一致：读不到就继续尝试下一个视图
                }
            }

            return null;
        }

        private static string PtrToString(IntPtr buffer, int length)
        {
            if (length <= 0) return string.Empty;
            var builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char c = (char)Marshal.ReadInt16(buffer, i * 2);
                if (c == '\0') break;
                builder.Append(c);
            }

            return builder.ToString();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetUserDefaultGeoName(IntPtr geoName, int geoNameCount);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetUserDefaultLocaleName(IntPtr localeName, int localeNameCount);

        [DllImport("kernel32.dll")]
        private static extern int GetUserPreferredUILanguages(uint flags, ref uint languageCount, IntPtr languageBuffer,
            ref uint bufferChars);

        [DllImport("kernel32.dll")]
        private static extern int GetUserGeoID(int geoClass);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetGeoInfoW(int location, int geoType, IntPtr geoData, int dataSize, int flags);
    }
}
