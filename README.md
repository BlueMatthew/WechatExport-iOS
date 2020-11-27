# C++重新实现了微信聊天记录导出程序，移除了对dotnet的依赖。
## https://github.com/BlueMatthew/WechatExporter
执行文件下载：
[Windows x64 Exe](https://github.com/BlueMatthew/WechatExporter/releases/download/v1.3/v1.3_x64_win.zip) [MacOS x64 App](https://github.com/BlueMatthew/WechatExporter/releases/download/v1.3/v1.3_x64_macos.zip)

  
 












## WechatExport-iOS
Save iOS WeChat history as HTML or TXT with neat layout and picture &amp; audio support.

将iOS上微信的聊天记录导出为包括图片和语音的HTML，或纯文本。

### 操作步骤：
1. 用iTunes将手机备份到电脑上（建议备份前杀掉微信），Windows操作系统一般位于目录：C:\用户\[用户名]\AppData\Roaming\Apple Computer\MobileSync\Backup\
2. 下载本代码的执行文件：[win x64](https://github.com/BlueMatthew/WechatExporter/releases/download/v1.0/x64_win.zip) 或者[MacOS x64](https://github.com/BlueMatthew/WechatExporter/releases/download/v1.0/x64_macos.zip)
3. 解压压缩文件
4. 执行解压出来的WechatExport.exe 
5. 按界面提示进行操作。

另外，解压目录下的res\templates子目录里存放了最终输出的聊天记录的html页面模版，通过两个%包含起来的，譬如，%%NAME%%，这样的字符串不要修改之外，其它页面格式都可以自行调整。

测试微信版本：7.0.15

### Mac版本
基于.net core 2.1实现了一个简单的命令行程序wxexp，可以在Mac环境下执行。命令如下：

dotnet wxexp.dll --backup \[iTunes Backup Path] --output \[History Output Path]

注意，程序依赖的ffmpeg和silk-v3-decoder需要自行编译（lib目录下的decoder可能不兼容），编译ffmpeg需要附带参数：--enable-libmp3lame

编译可参考：[https://stackoverflow.com/questions/42337870/how-to-convert-silk-audio-file-from-wechat-to-amr-format]

The only open-source one-click application that parses the local database of Wechat, the most popular chatting app in China. This software bypasses the sandbox restriction recently introduced in iOS, and obtain Wechat app's data from an iTunes backup. It then links together data in SQLite files and various assets such as images, audios (format conversion involved), videos, etc. Users get a series of well-formated HTML files of their chat history, so that they can read later on any browsers.

Download latest stable binary here: 在这里下载最新的打包好的程序：  
win x64: https://github.com/BlueMatthew/WechatExporter/releases/download/v1.3/v1.3_x64_win.zip  
MacOS x64: https://github.com/BlueMatthew/WechatExporter/releases/download/v1.3/v1.3_x64_macos.zip
