内置图片查看器设定（仿picasa3交互的显示状态）
==================================================

## 窗口与背景
1、窗口设置2种背景色，一种#FAFAFA纯色（普通模式），一种#99000000深色半透明（全屏模式）
2、普通窗口状态样式：显示标题栏，白色背景色，窗口默认尺寸为屏幕工作区的50%
3、全屏显示状态：将白色背景更换为半透明深灰色背景，隐藏标题栏，
   窗口铺满屏幕工作区（非 WindowState.Maximized，手动定位以避开分层窗口限制）

## 布局结构
- 窗口级 Grid 含两个子元素：MainContentBorder（图片内容区）+ FullScreenOverlay（全屏底部工具栏）
- MainContentBorder 内 Grid 分两行：Row 0 标题栏(35px) + Row 1 图片区
- Image 在 Row 1 中 VerticalAlignment=Center，Stretch=Uniform，通过 RenderTransform 实现缩放/平移/旋转
- 全屏时 MainContentBorder 锁定为固定尺寸 + Margin，图片屏幕位置不变（方案 Spro）
- WindowChrome 在 XAML 中固定不变，根除 DWM 重绘闪烁

## 全屏↔普通切换
2、普通窗口与全屏状态切换时，图片在屏幕上的位置不动，且不发生闪烁。
3、全屏状态下对图片进行平移、缩放或旋转后，切换至普通窗口时图片仍位置不动、不闪烁。
4、两种状态按 ESC 均退出图片查看器，两种状态是并列的。
5、切换方式：Enter 键 / 双击图片区域 / 标题栏最大化按钮；
   全屏状态下鼠标左键单击空白区域也可切换回普通状态。

## 普通窗口交互
6、普通窗口里的部分交互设定：
（1）空格键：图片在窗口中复位到初始比例显示
（2）"0"或"小键盘0"：窗口边框贴合图片（先重置 zoom=1.0，再计算窗口尺寸，确保精确贴合）
（3）鼠标右键单击空白区域：执行窗口边框贴合图片
（4）鼠标左键拖动窗口空白区域：平移窗口

## 初始化缩放
- 普通窗口直接打开图片：_zoomFactor = 1.0f，图片填满窗口（超出窗口的维度留白，不再有底部异常空白）
- 全屏模式直接打开图片：_zoomFactor = 0.9f，图片占屏幕工作区的90%
  → 动画入场：Border 从屏幕中心极小尺寸(2px) EaseOut 扩展至全屏(500ms)，全屏覆盖层动画结束后显示

## 技术实现要点
- FitWindowToImage() 调用前先重置 zoom=1.0 + pan=0，确保窗口尺寸计算与图片显示尺寸一致
- 首图加载通过 _firstImageLoadedTcs 信号通知，全屏动画入场等图片就绪后才启动
- EnterFullScreenAnimatedAsync 独立于 ToggleFullScreen，仅用于全屏初始打开场景
- ImageViewerLauncher 全屏启动分支改用 EnterFullScreenAnimatedAsync 替代 ToggleFullScreen

