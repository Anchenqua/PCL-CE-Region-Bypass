using System;
using System.Collections.Generic;
using PclRegionBypass;

namespace PolicyLogicTest
{
    /// <summary>
    ///     判定逻辑的确定性单元测试：直接把构造好的 RegionSignals 喂给移植版 Evaluate，
    ///     不读本机任何信号，因此在 zh-CN / en-US 的机器或 CI runner 上结果都一样。
    ///     退出码 0 = 全部通过。
    /// </summary>
    internal static class Program
    {
        private static int Main()
        {
            try
            {
                Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            }
            catch
            {
                // 忽略
            }

            int failures = 0;

            // 1) 地区名是中国 → 受限
            Expect(ref failures, "geo=CN", RegionClassification.Mainland, "geo-cn",
                Signals("CN", null, "zh-CN", "China Standard Time"));

            // 2) 地区名是 US，但 OOBE 设备区域 = 45（中国大陆）→ 受限
            Expect(ref failures, "device-region=45", RegionClassification.Mainland, "device-region-cn",
                Signals("US", 45, "en-US", "Pacific Standard Time"));

            // 3) 地区名 US + 非中文区域 + 非中国时区 → 放行
            Expect(ref failures, "geo=US 正常放行", RegionClassification.Other, "geo-other",
                Signals("US", 244, "en-US", "Pacific Standard Time"));

            // 4) 地区名 US，但时区是中国 + 区域是 zh-CN → 受限（PR 之后追加的第二道检查）
            Expect(ref failures, "china-tz + zh-CN", RegionClassification.Mainland, "china-time-zone-and-locale",
                Signals("US", 244, "zh-CN", "China Standard Time"));

            // 5) 时区中国、区域 en-US → 不算（第 3 条要求时区与语言同时满足）
            Expect(ref failures, "china-tz + en-US 放行", RegionClassification.Other, "geo-other",
                Signals("US", 244, "en-US", "China Standard Time"));

            // 6) 所有信号都拿不到 → 未知，按受限处理
            var empty = new RegionSignals
            {
                GeoNameAvailable = false,
                PreferredUiLanguagesAvailable = false,
                UserLocaleAvailable = false,
                TimeZoneAvailable = false,
                DeviceRegionAvailable = false
            };
            Expect(ref failures, "无信号 → 未知/受限", RegionClassification.Unknown, "signals-unavailable", empty);

            // 7) 只有地区名、没有设备区域时不应该误判
            Expect(ref failures, "geo=JP", RegionClassification.Other, "geo-other",
                Signals("JP", null, "ja-JP", "Tokyo Standard Time"));

            if (failures == 0)
            {
                Console.WriteLine("POLICY: ALL PASSED");
                return 0;
            }

            Console.WriteLine("POLICY: " + failures + " FAILED");
            return 1;
        }

        private static RegionSignals Signals(string geo, int? deviceRegion, string locale, string timeZone)
        {
            return new RegionSignals
            {
                GeoName = geo,
                GeoNameAvailable = true,
                PreferredUiLanguages = new List<string> { locale },
                PreferredUiLanguagesAvailable = true,
                UserLocaleName = locale,
                UserLocaleAvailable = true,
                TimeZoneId = timeZone,
                TimeZoneAvailable = true,
                DeviceRegionGeoId = deviceRegion,
                DeviceRegionAvailable = deviceRegion.HasValue
            };
        }

        private static void Expect(ref int failures, string name, RegionClassification classification, string reason,
            RegionSignals signals)
        {
            RegionAssessment result = RegionPolicyPort.Evaluate(signals);
            bool ok = result.Classification == classification && result.Reason == reason;

            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name + " → " + result.ClassificationText + " / " +
                              result.Reason + (ok ? string.Empty
                                  : "  (expected " + classification + " / " + reason + ")"));

            if (!ok) failures++;
        }
    }
}
