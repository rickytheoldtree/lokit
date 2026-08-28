## 使用说明

### 环境要求
- Windows
- [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)
- 本机装有 Edge / Chrome / Firefox 任意一个（Windows 自带 Edge，通常无需额外安装）

### 浏览器驱动
翻译功能使用 `Selenium`。**驱动不需要手动准备**：程序会自动探测本机浏览器版本，
通过 Selenium Manager 下载版本匹配的驱动并缓存到 `data/web-drivers`，之后直接复用。

- 设置页「浏览器」默认为**自动**，会挑选本机已安装的浏览器（优先 Edge）；也可以手动指定。
- 若下载失败（网络受限），在设置页填写「驱动代理」后重试，例如 `http://127.0.0.1:7890`。
- 也可以手动把对应驱动（`msedgedriver.exe` / `chromedriver.exe` / `geckodriver.exe`）
  放进 `data/web-drivers`，程序在联网失败时会回退使用它。
