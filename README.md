## WechatExport-iOS
Save iOS WeChat history as HTML or TXT with neat layout and picture &amp; audio support.

将iOS上微信的聊天记录导出为包括图片和语音的HTML，或纯文本。

操作步骤：
1. 用iTunes将手机备份到电脑上，Windows操作系统一般位于目录：C:\用户\[用户名]\AppData\Roaming\Apple Computer\MobileSync\Backup\
2. 下载本代码的执行文件：https://github.com/BlueMatthew/WechatExport-iOS/releases/download/1.1.0/release-1.1.0.zip
3. 解压压缩文件
4. 执行解压出来的WechatExport.exe
5. 按界面提示进行操作。

另外，解压目录下的res\templates子目录里存放了最终输出的聊天记录的html页面模版，通过两个%包含起来的，譬如，%%NAME%%，这样的字符串不要修改之外，其它页面格式都可以自行调整。


The only open-source one-click application that parses the local database of Wechat, the most popular chatting app in China. This software bypasses the sandbox restriction recently introduced in iOS, and obtain Wechat app's data from an iTunes backup. It then links together data in SQLite files and various assets such as images, audios (format conversion involved), videos, etc. Users get a series of well-formated HTML files of their chat history, so that they can read later on any browsers.

Download latest stable binary here: 在这里下载最新的打包好的程序：
https://github.com/BlueMatthew/WechatExport-iOS/releases/download/1.1.0/release-1.1.0.zip
