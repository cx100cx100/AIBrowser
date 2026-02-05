# AIBrowser - AI 聚合浏览器 🚀

一个基于 WebView2 (Edge) 内核的轻量级 AI 聚合浏览器。专为高效使用 ChatGPT、Claude、Gemini、Kimi 等 AI 工具而设计。

用途：期望一站式使用固定的几个网页，如一站式使用全部的 AI 网站。

![AIBrowser 截图](Assets/screenshot.png) 
## ✨ 主要功能

* **⚡ 聚合入口**：内置 ChatGPT、Gemini、Kimi、腾讯元宝等主流 AI 网站，一键切换。
* **🛠️ 高度自定义**：支持自定义添加、删除、排序常用的 AI 网站。
* **🎨 主题支持**：支持深色模式、浅色模式及跟随系统自动切换。
* **🧠 智能管理**：
    * **LRU 内存优化**：自动清理长期不用的标签页内存，保持系统流畅。
    * **自动标题获取**：自动抓取网页标题作为标签名。
    * **图标自动缓存**：智能获取并本地缓存网站图标 (Favicon)。
* **💼 摸鱼/老板键**：支持全局快捷键 `Alt + A` 快速显示/隐藏窗口。
* **👻 托盘运行**：支持最小化到系统托盘，保持桌面整洁。
* **💾 便携绿色**：提供免安装便携版，解压即用。

## 🛠️ 技术栈

* **.NET 6/8 (WPF)**
* **WebView2 (Microsoft Edge Chromium)**
* **C#**

## 🚀 快速开始

### 安装版
1. 下载最新的 `Setup.exe`。
2. 双击安装即可。

### 便携版
1. 下载 `AIBrowser_Portable.zip`。
2. 解压到任意目录。
3. 运行 `AIBrowser.exe`。

## 📝 开发与构建

如果你想自己编译本项目：

1. 克隆仓库：
   ```bash
   git clone [https://github.com/你的用户名/AIBrowser.git](https://github.com/你的用户名/AIBrowser.git)
  
2. 使用 Visual Studio 2022 打开 AIBrowser.sln。

确保安装了 .NET 桌面开发 工作负载。

按 F5 运行。

## 📄 许可证
本项目采用 MIT 许可证。



