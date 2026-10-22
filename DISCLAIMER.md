# 免责声明 / Disclaimer

> 简体中文在前，English summary at the end.

## 一、性质与关联

1. 本项目（PCL-CE 地区限制解除器，下称"本工具"）是一个**独立的技术实验项目**，用于研究
   Windows 上「区域/语言信号」是如何影响软件行为的，以及 .NET 桌面程序在运行时可以被如何观测。
2. 本工具与 **PCL-CE / Plain Craft Launcher 2 的开发者、Mojang Studios、Microsoft、网易（NetEase）**
   均**没有任何隶属、授权、认可或支持关系**。PCL-CE 的著作权归其各自作者所有。
3. 本工具**不包含** Minecraft 游戏本体、资源文件、库文件，也**不包含**任何被修改过的启动器副本。
   补丁只发生在**你自己机器上、你自己启动的那个进程**的内存里，进程退出即消失。

## 二、技术风险

4. 本工具通过改写目标进程内若干 Win32 函数入口（写入 5 字节跳转）来实现，属于**非官方的运行时补丁**。
   它理论上可能导致：启动器行为异常、功能失效、崩溃、未保存数据丢失。
5. 本工具会临时冻结目标进程的线程以完成写入，并以固定签名识别"是否已打过补丁"。
   尽管如此，**在启动器运行期间使用本工具仍存在不可预知的风险**，请自行评估。
6. 若目标进程正在执行下载/安装等写盘操作时被强制处理，可能产生不完整的文件。
   建议在启动器空闲时附加，或直接用本工具启动启动器。

## 三、合规与授权（重要）

7. **本工具解除的是"地区可用性判断"，不是"授权"。** Minecraft Java 版本体需要你自己合法购买与持有。
   本工具**不能**、也**不是**用来绕过正版验证、账号验证或付费流程的。
8. PCL-CE 之所以加入这项限制，是上游为应对**发行与授权区域的合规要求**而做的改动。
   在受该限制约束的地区绕过它，**可能违反 PCL-CE 的使用条款、Mojang/Microsoft 的服务条款，
   或你所在国家/地区的法律法规**。
9. **是否使用、如何使用，由你自行判断并承担全部后果。** 如果你不确定自己是否有权这样做，请不要使用。
10. **禁止**将本工具或经其处理的启动器用于：商业用途、二次分发"解锁版"、任何形式的盗版或规避授权、
    以及任何违法用途。

## 四、担保与责任

11. 本工具按**"现状"（AS IS）**提供，不附带任何明示或暗示的担保，包括但不限于适销性、
    特定用途适用性与不侵权担保。
12. 在适用法律允许的最大范围内，**作者与贡献者不对因使用或无法使用本工具而产生的任何直接、
    间接、偶然、特殊或后果性损失承担责任**（包括但不限于数据丢失、设备损坏、账号封禁、
    法律纠纷、商业损失）。
13. 你使用本工具即表示你已阅读、理解并同意本免责声明的全部内容。若不同意，请立即停止使用并删除本工具。

## 五、权利与联系

14. 本工具以 Apache License 2.0 开源，见 [LICENSE](LICENSE) 与 [NOTICE](NOTICE)。
15. 若相关权利人认为本项目的内容侵犯其权益，请通过仓库的 Issue 联系，我们会在核实后及时处理
    （修改、署名或移除相关内容）。
16. 本免责声明可能随项目更新而调整，以仓库中的最新版本为准。

---

## English summary

This is an **unofficial, independent** technical experiment. It is **not affiliated with, endorsed by, or
supported by** PCL-CE, Mojang Studios, Microsoft, or NetEase.

The tool patches a handful of Win32 function entries **inside the memory of the launcher process you start
on your own machine**; nothing on disk is modified and the effect disappears when that process exits.
It does **not** contain or distribute Minecraft, nor any modified launcher build.

**It does not grant any license.** Minecraft Java Edition requires a valid, legally obtained license.
Removing a *regional availability check* may violate the terms of service of the upstream project and/or the
laws of your jurisdiction — the upstream restriction exists for compliance reasons. **You are solely
responsible** for deciding whether you may use this, and for all consequences.

Provided **AS IS**, without warranty of any kind. The authors and contributors accept **no liability** for any
damages or legal consequences arising from its use. Commercial use, redistribution of "unlocked" launcher
builds, and any use to circumvent licensing or payment are **prohibited**.

Licensed under the Apache License 2.0 (see [LICENSE](LICENSE) and [NOTICE](NOTICE)).
