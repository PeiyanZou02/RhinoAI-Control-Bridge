# Rhino to Comfy

[English](README.md)

这是一个面向 Rhino 8 和 ComfyUI 的本地控制图桥接插件。它把 Rhino 当前视角导出为深度、法线、轮廓、材质分区、形状锁定、Wallpaper 定位及蒙版等图片，并自动更新到 ComfyUI 中指定的 **Batch Images** 节点。

同步功能只更新图片和输入说明，不会点击 Run、提交 Prompt 或加入生成队列。只有你主动启动时才会生成：在 ComfyUI 中点击 Run，或在 **AI render** 页面点击 **Render ticked views**——后者用厂商 API 直接渲染 Rhino 已命名视图，不需要 ComfyUI。

## 主要功能

- 保持当前 Rhino 视口的相机、透视、比例和视锥。
- 导出 Rendered、Shape Lock、Depth、Normal、Edges、Silhouette、Mask、Base Color、Object ID 和 Material ID。
- Material ID 严格按照 Rhino 图层区分；需要分开的材质部件应放在不同图层。
- 图层颜色可映射为目标材质，并同步简短的 `#HEX = 目标材质`。
- 支持把 Rhino Wallpaper 作为背景照片，将 Rhino 对象融合进场地、街景、室内或人像等任意底图。
- 自动匹配 Portrait 或 Landscape Wallpaper 的原图比例，裁掉 Rhino 视口留白，同时保持相机位置、镜头和 Perspective 光线一致。
- 导出 Placement、Scale Lock、Product Mask、Inpaint Mask 和可选遮挡蒙版。
- 对象在全画幅中很小时，额外生成同一相机透视下的高清局部几何控制图。
- 针对 Nano Banana 多图输入自动限制为不超过 14 张。
- 保留用户在 ComfyUI 中自己填写的 Prompt。
- 所有图片上传成功后才切换到新批次，避免半套输入。

## 环境要求

- Windows 10/11
- Rhino 8
- 本机 ComfyUI 或 ComfyUI Desktop
- 从源码构建时需要 PowerShell 7 或系统自带的 Windows PowerShell 5.1
- 支持多图输入的 ComfyUI 生图节点或工作流

默认服务地址为 `http://127.0.0.1:8000`。如果你的 ComfyUI 使用 `8188` 等其他端口，可直接在 Rhino 面板中修改。

## 安装

### Rhino 插件

1. 下载 `dist/Rhino2Comfy.rhp`，或者从源码构建。
2. 把 `.rhp` 拖入 Rhino 8，或运行 `PlugInManager`，点击 **Install** 并选择该文件。
3. 如有提示，重启 Rhino。
4. 在 Rhino 命令栏运行 `Rhino2Comfy`。

命令 `Rhino2ComfyExport` 会按已保存设置直接导出，但不会打开插件面板。

### ComfyUI 同步扩展

在项目目录中运行：

```powershell
.\install-comfy-extension.ps1
```

脚本默认安装到当前 Windows 用户的“文档/ComfyUI”。若 ComfyUI 位于其他目录：

```powershell
.\install-comfy-extension.ps1 -ComfyRoot "D:\ComfyUI"
```

首次安装后重启 ComfyUI。只更新前端文件时，先保存工作流，再在 ComfyUI Desktop 画布中按 **F5**。

## 面板

运行 `Rhino2Comfy` 打开面板。面板为英文界面，包含五个页面和一条操作栏。

| 页面 | 内容 |
| --- | --- |
| **Sync** | 目标工作流、ComfyUI 实时状态行、仅导出选中对象、自动上传 |
| **AI render** | 引擎与 API Key、要渲染的已命名视图、要发送的控制图、提示词和渲染输出目录 |
| **Layer materials** | 每个 Rhino 图层一行，显示编码色，并填写目标材质 |
| **Background blend** | 把 Rhino 对象融合进视口 Wallpaper 背景照片 |
| **Export settings** | ComfyUI 地址、导出目录、可选的 API 工作流、输出尺寸和固定宽屏画幅 |

**Update to ComfyUI** 会导出并发布图片。**Export only** 只写文件，不连接 ComfyUI。鼠标悬停在任意选项上可以看到简短说明。

## 日常使用

1. 启动 ComfyUI，打开包含 Batch Images 并连接生图模型的工作流。
2. 在 Rhino 中打开模型并激活要导出的视口。
3. 输入 `Rhino2Comfy` 打开插件。
4. 检查图层与目标材质表。不同 Material ID 的部件放入不同 Rhino 图层。
5. 在 **Sync** 页的 **Target workflow** 下拉框中选择要更新的 ComfyUI 文件，或保持 **Follow the current ComfyUI window**。
6. 在 Rhino 中点击 **Update to ComfyUI**。如果选择了目标文件，ComfyUI 会自动切到该文件再更新。
7. 在 ComfyUI 中填写或修改自己的 Prompt。
8. 由你手动点击 **Run**。

### 选择目标工作流

下拉框列出 ComfyUI 中已保存的全部工作流，按修改时间倒序排列，点击 **Refresh** 可重新读取。

- 选择具体文件后，每次点击 **Update to ComfyUI**，ComfyUI 会打开或切到该文件的标签页，再更新其中的 Batch Images。原先标签页中未保存的修改会保留。
- 如果你随后自己切到别的文件，那个文件不会被改动。ComfyUI 底部的 Rhino 按钮会显示 "target is X · click to switch"。
- 自动同步只上传图片，不会切换 ComfyUI 窗口。
- 下拉框下方的状态行显示 ComfyUI 当前显示的文件，以及已接入的图片数量。

### 在新工作流中接入

在没有 Batch Images 节点的工作流里点击 ComfyUI 底部的 Rhino 按钮，会在画布中央插入一个 Batch Images 节点和全部 Rhino 图片节点。插件不会创建或假设任何生图节点，把 Batch Images 的输出连到你使用的任意模型或 API 节点即可。

工作流中已有多个 Batch Images 时，先选中要接入的那一个，再点击 Rhino 按钮。

更换模型、视角、选择或材质后不会自动生成。再次点击 **Update to ComfyUI** 即可发布新的输入图片。

普通场景模式保持原来的 9 张控制图，并默认开启**Lock widescreen frame 1024 × 589**。调整 Rhino 窗口、打开侧边栏或移动插件窗口都不会再把输出变成正方形；插件会对所有控制图使用同一个相机子视锥，Perspective 保持一致。只有开启 Wallpaper 产品佩戴模式时，才会使用参考底图、placement、scale-lock、局部放大和 inpaint 规则。

## AI 渲染：不经过 ComfyUI 批量渲染已命名视图

ComfyUI 的生图节点本质上是对厂商 API 的封装。只要有自己的 API Key，**AI render** 页面就能直接调用厂商接口，并把结果保存到本地目录。

1. 用 Rhino 的 `NamedView` 命令保存需要的相机视角。
2. 在 **AI render** 页面选择 **Engine**（引擎）：

| 引擎 | 调用的接口 | Key 环境变量 |
| --- | --- | --- |
| Google Gemini (Nano Banana) | `models/<model>:generateContent` | `GEMINI_API_KEY` |
| OpenAI (GPT Image) | `images/edits` | `OPENAI_API_KEY` |
| Volcengine Doubao (Seedream，火山引擎豆包) | `images/generations` | `ARK_API_KEY` |
| ComfyUI | 把 Export settings 中的 **API workflow** 加入队列，并下载它保存的图片 | 无 |

3. 粘贴 API Key；留空则读取对应的环境变量。每个引擎各自保存 Key、模型和地址。模型列表可以直接输入，方便使用更新的模型。只有使用代理或兼容网关时才需要改 API address。
4. 选择 **Aspect ratio**（画幅比例）和 **Resolution**（分辨率）。`auto` 跟随导出画幅和 Longest edge 设置。下拉列表只显示当前引擎和模型实际支持的选项：

| 引擎 | 画幅比例 | 分辨率 |
| --- | --- | --- |
| Gemini 3 图像模型 | 1:1、2:3、3:2、3:4、4:3、4:5、5:4、9:16、16:9、21:9 | 1K、2K、4K |
| Gemini 2.5 Flash Image | 同上 | 由模型固定 |
| OpenAI GPT Image | 1:1、3:2、2:3（固定尺寸） | 质量：low、medium、high |
| 豆包 Seedream 4.0 / 4.5 | 与 Gemini 相同，按像素尺寸发送 | 1K（仅 4.0）、2K、4K |

   厂商只支持这些固定比例，所以 AI render 会直接按引擎将要使用的比例导出 Rhino 画幅，而不是用保存的宽屏画幅。`auto` 时取最接近的支持比例：默认的 1024 : 589 在 Gemini 下变成 16:9，在 GPT Image 下变成 3:2。相机、镜头和透视不变，只改变画幅的裁切，并且只对这一次渲染生效。厂商返回的取整画布（例如 2752 × 1536）会被两侧均匀裁掉多余部分，保存的结果与导出比例完全一致。Seedream 接受任意像素尺寸，无需调整。背景图融合模式保持 Wallpaper 的比例。

5. 勾选要渲染的已命名视图。**(Current viewport)** 表示按当前视角渲染。**Reload views** 重新读取 Rhino 中的视图列表。
6. 勾选要发送的控制图，填写提示词，并选择 **Render folder**（输出目录）。
7. 点击 **Render ticked views**。插件会依次在当前视口恢复每个视图、导出控制图，连同提示词、图片角色说明和图层材质映射一起发送，并保存为 `<视图名>_<时间>.png`。全部完成后恢复原来的视角。**Cancel** 会中止当前请求并停止批处理；某个视图失败会记录在日志里，其余视图继续。

### 风格参考图（Style reference photos）

在 **Style reference photos** 里最多添加 4 张真实照片，让结果更像照片。它们会跟在控制图后面随每个视图一起发送，并附带说明：只借用照片的“观感”——光线质感、曝光、调色、材质真实感、氛围、景深和镜头特征。几何、相机和材质分区仍然只来自 Rhino 控制图，所以参考图不会改动你的设计。

- 选择与目标效果题材、光线接近的照片，例如建筑用建筑摄影，产品用棚拍产品图。一两张风格一致的照片比四张不同风格的效果好。
- 照片会复制到每次导出目录，命名为 `style_reference_N.jpg`，自动转正并缩小到最长边 1536 px；原图不会被改动，请求体积也保持较小。
- 支持 JPG、PNG、BMP。找不到的文件会在日志里提示并跳过。
- 在 ComfyUI 工作流里同样可用。**Sync** 页面勾选 **Send the style reference photos** 后，**Update to ComfyUI** 会把它们排在控制图后面一起上传，以 `RHINO:style_reference_1` 等节点接入 Batch Images，并在托管提示词里写入同一条“只借用风格”的规则。如果一批已经有 14 张图，参考图会被省略。需要同步扩展版本 24：重新运行 `install-comfy-extension.ps1`，重启 ComfyUI 并按 **F5**。
- AI render 使用 ComfyUI 引擎时同样会上传，并绑定到这些标题的 `LoadImage` 节点。
- 在背景图融合模式下，Wallpaper 照片仍然决定整幅画面的观感，风格参考图只影响插入物体材质的真实感。

每个视图实际发送的提示词保存在该视图导出目录的 `ai_prompt.txt`。最长边、锁定宽屏画幅、仅导出选中对象、背景图融合等导出选项对每个视图同样生效。背景图融合模式固定发送自己的参考图、定位图和蒙版。

使用 ComfyUI 引擎时，API 工作流里除了原有的 `RHINO:<通道>` 图片节点，还可以用 `{{positive_prompt}}` 代表你的提示词，用 `{{full_prompt}}` 代表带图片角色和材质映射的完整提示词。

厂商 API 按生成张数由厂商计费。

### 画幅比例（Frame ratio）

Nano Banana 这类图像模型只支持几种固定比例：1:1、2:3、3:2、3:4、4:3、4:5、5:4、9:16、16:9、21:9。其他比例的画幅（包括 1024 × 589 宽屏画幅）会让模型拉伸或重新构图。所以 Export settings 页面的 **Frame ratio** 默认是 **auto**，按最接近的支持比例导出：宽屏画幅变成 16:9，2048 px 时导出 2048 × 1152。它对 **Update to ComfyUI**、**Export only** 和 `Rhino2ComfyExport` 都生效。相机、镜头和透视不变，只改变画幅的裁切。

- 把 ComfyUI 里模型节点的比例设成同一个，或者设成 `auto`。
- 选择某个固定比例可以强制使用它；选 **Locked frame or viewport, unchanged** 则恢复以前的行为。
- AI render 优先使用所选引擎自己的比例；引擎接受任意尺寸时才回退到这个设置。
- 背景图融合模式保持 Wallpaper 的比例。

### 图片尺寸与线宽

Export settings 页面的 **Longest edge** 决定所有控制图的尺寸，默认 2048 px。锁定宽屏画幅在任何尺寸下都保持 1024 : 589 的比例，所以 2048 导出的是 2048 × 1178。以前按旧默认值 1024 保存的设置会自动改为 2048 一次；之后你自己设定的数值会保留。

`edges`、`silhouette`、`lineart` 以及 `shape_lock` 的墨线都是 1 像素宽，并画在靠前的那个表面上，椅子腿、板条这类细小构件不会再粘成一团。超过约 2300 px 时线宽会增加到 2、3 像素，避免被模型自身的缩放抹掉。

## 背景图融合（Background blend）

1. 使用 Rhino 的 `Wallpaper` 命令，把背景照片（场地、街景、室内或人像等）放入当前视口。
2. 移动 Rhino 对象并调整相机，使其对准照片中的目标位置和透视。
3. 选中要融合的对象，并开启 **Export selected objects only**。
4. 在 **Background blend** 页开启 **Blend into the Wallpaper background**，填写融合要求。
5. 导出并更新到 ComfyUI。

全画幅 `reference` 和 `placement` 决定最终构图、位置与尺度。只有 `reference` 可以决定最终画面的彩色或黑白模式、曝光、对比度、明暗层次、色彩和背景，其他技术控制图不能让结果染色或变灰。`scale_lock.png` 在完整画幅中标记对象的精确轮廓、外接框和中心；其底图现在保持 reference 的原始明暗，其中紫色轮廓、矩形框和十字仅是不可见的测量信息，绝不能出现在最终成图中。自动提示词会在图片职责说明的开头和结尾重复外观锁定与清理要求。放大的 `*_detail` 图片只提供造型与材质细节，不能改变最终产品的大小和位置。

保持 **Match the Wallpaper aspect ratio** 开启时，Portrait 参考图会直接生成竖幅结果。插件会先以足够的中间分辨率捕获完整 Rhino 视口，再裁出对应的相机子视锥，并把同一竖幅画布用于 reference、placement、rendered、全部几何控制图及蒙版；不会拉伸或旋转照片。

若需要严格保持蒙版外像素，应在工作流中使用 `inpaint_mask.png`，并把生成区域重新合成到 `reference.png`。单靠文字提示不能保证蒙版外每一个像素完全不变。

## 主要导出文件

| 文件 | 用途 |
| --- | --- |
| `rendered.png` | 当前相机下的 Rhino Rendered 显示 |
| `shape_lock.png` | 高对比造型参考 |
| `depth.png` | 近白远黑的线性相机深度 |
| `depth_inverse.png` | 反向深度，背景保持黑色 |
| `normal.png` | 相机空间表面法线 |
| `edges.png` | 黑底几何边缘 |
| `silhouette.png` | 前景外轮廓 |
| `mask.png` | 可见几何前景蒙版 |
| `material_id.png` | 严格按照 Rhino 图层分配的材质区域 |
| `basecolor.png` | 不含灯光的平面材质底色 |
| `reference.png` | 完整 Wallpaper 参考底图 |
| `placement.png` | 对象在参考图上的全画幅位置 |
| `scale_lock.png` | 仅用于全画幅尺度与位置测量，不作为最终外观 |
| `object_mask.png` | 对象在完整画幅中的精确轮廓 |
| `inpaint_mask.png` | 平滑的允许编辑邻域 |
| `blend.json` | 位置边界、相机、图片职责和约束信息 |

同一次导出的基础控制图使用完全相同的相机和透视。Portrait 输出和局部细节图都使用从原视角裁切出的相机子视锥，不改变相机位置或镜头，因此 Perspective 保持一致。

Material ID 现在使用黑色背景，并严格跟随 Rhino 图层显示颜色。插件会把完全相同的 HEX 编码写进材质映射 Prompt。相邻部件不容易区分时，可以点击 **Assign contrast colors**。正在使用的图层必须采用不同的非黑色颜色，避免材质区域含混或消失。

## 构建与测试

```powershell
.\build.ps1
.\build.ps1 -Tests
node .\tests\sync-core.test.mjs
```

构建脚本使用已安装的 Rhino 8 `RhinoCommon.dll`。在 PowerShell 7 中使用其自带的 Roslyn 编译器；在 Windows PowerShell 5.1 中自动改用 Rhino 8 自带的 Roslyn，因此无需另装 PowerShell 7。输出文件为 `dist/Rhino2Comfy.rhp`。

同步测试会验证插件只上传和发布输入，不调用 ComfyUI 的 Prompt 或 Queue 接口。

## 项目结构

```text
src/        Rhino 插件源码
comfyui/    ComfyUI 同步扩展
workflows/  示例工作流/API JSON
tests/      C# 与 JavaScript 同步测试
scripts/    工作流辅助脚本
dist/       最新预编译 Rhino 插件
```

## 隐私与限制

- 同步功能只把图片发送到面板中配置的 ComfyUI 地址，不会发送任何 API Key。
- AI 渲染只在你点击 **Render ticked views** 时，把勾选的控制图和提示词发送到该页面显示的 API 地址。API Key 用 Windows DPAPI 按当前 Windows 账户加密后保存在本机 `settings.json`，只通过 HTTPS 请求头发送，不会写入日志或导出目录。
- 导出图片、本机设置和测试输出不会提交到 Git。
- Nano Banana 等参考图模型仍属于生成式模型。Scale Lock 和图片职责能提高一致性，但不等于 ControlNet 的数学像素锁定。
- 当前几何控制图把透明表面视为不透明；不会把贴图、AO、roughness、metalness、displacement、剖切平面或未烘焙 Grasshopper 预览作为原生渲染通道导出。

参考资料：[RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/) 与 [ComfyUI 服务端接口](https://docs.comfy.org/development/comfyui-server/comms_routes)。
