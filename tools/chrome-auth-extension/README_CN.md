# CliRelay OAuth Chrome 插件

这个插件用于从 Chrome 里启动 CliRelay 的 OAuth 登录，并自动捕获 `http://localhost:1455/auth/callback?...` 这类回调地址提交给 CliRelay。

## 安装

方式 A：双击 `start-chrome-auth-extension.bat` 或 `start-clirelay-oauth-auto.bat`，脚本会自动监听 OAuth 回调、打开 Chrome 授权页，并把回调提交给 CliRelay。

方式 B：手动加载：

1. 打开 `chrome://extensions/`
2. 开启开发者模式
3. 点击“加载已解压的扩展程序”
4. 选择 `D:\chengmao\re-x\CliRelay\tools\chrome-auth-extension`

## 使用

1. 确认 CliRelay 正在运行，默认管理接口是 `http://127.0.0.1:8317/v0/management`
2. 在插件里填写管理密钥，当前本机默认是 `admin123`
3. 双击启动脚本后会自动测试连接、启动 OAuth，并打开 Chrome 授权页
4. 在打开的页面完成登录授权
5. 插件会自动捕获 localhost 回调并提交；如果没有自动捕获，把浏览器地址栏里的完整回调地址粘到“手动回调”再提交

支持的提供商：Codex、Anthropic、Antigravity、Gemini CLI、Qwen、Kimi。
