# Rhino AI Control Bridge

[English](README.md)

这是一个面向 Rhino 8 和 ComfyUI 的本地控制图桥接插件。它把 Rhino 当前视角导出为深度、法线、轮廓、材质分区、形状锁定、Wallpaper 定位及蒙版等图片，并自动更新到 ComfyUI 中指定的 **Batch Images** 节点。

插件只更新图片和输入说明，不会点击 Run、提交 Prompt 或加入生成队列。最终生成始终由用户在 ComfyUI 中手动启动。

## 主要功能

- 保持当前 Rhino 视口的相机、透视、比例和视锥。
- 导出 Rendered、Shape Lock、Depth、Normal、Edges、Silhouette、Mask、Base Color、Object ID 和 Material ID。
- Material ID 严格按照 Rhino 图层区分；需要分开的材质部件应放在不同图层。
- 图层颜色可映射为目标材质，并同步简短的 `#HEX = 目标材质`。
- 支持 Rhino Wallpaper 人物、手部和颈部参考图。
- 自动匹配 Portrait 或 Landscape Wallpaper 的原图比例，裁掉 Rhino 视口留白，同时保持相机位置、镜头和 Perspective 光线一致。
- 导出 Placement、Scale Lock、Product Mask、Inpaint Mask 和可选遮挡蒙版。
- 产品在全画幅中很小时，额外生成同一相机透视下的高清局部几何控制图。
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

1. 下载 `dist/RhinoAI.rhp`，或者从源码构建。
2. 把 `.rhp` 拖入 Rhino 8，或运行 `PlugInManager`，点击 **Install** 并选择该文件。
3. 如有提示，重启 Rhino。
4. 在 Rhino 命令栏运行 `RhinoAI`。

命令 `RhinoAIExport` 会按已保存设置直接导出，但不会打开插件面板。

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

## 日常使用

1. 启动 ComfyUI，打开包含 Batch Images 并连接生图模型的工作流。
2. 在 Rhino 中打开模型并激活要导出的视口。
3. 输入 `RhinoAI` 打开插件。
4. 检查图层与目标材质表。不同 Material ID 的部件放入不同 Rhino 图层。
5. 在“同步”页的 **目标工作流** 下拉框中选择要更新的 ComfyUI 文件，或保持“跟随 ComfyUI 当前窗口”。
6. 在 Rhino 中点击“更新到 ComfyUI”。如果选择了目标文件，ComfyUI 会自动切到该文件再更新。
7. 在 ComfyUI 中填写或修改自己的 Prompt。
8. 由你手动点击 **Run**。

### 选择目标工作流

下拉框列出 ComfyUI 中已保存的全部工作流，按修改时间倒序排列，点击“刷新”可重新读取。

- 选择具体文件后，每次点击“更新到 ComfyUI”，ComfyUI 会打开或切到该文件的标签页，再更新其中的 Batch Images。原先标签页中未保存的修改会保留。
- 如果你随后自己切到别的文件，那个文件不会被改动。ComfyUI 底部的 Rhino 按钮会显示“目标是 X · 点击切换”。
- 自动同步只上传图片，不会切换 ComfyUI 窗口。
- 下拉框下方的状态行显示 ComfyUI 当前显示的文件，以及已接入的图片数量。

### 在新工作流中接入

在没有 Batch Images 节点的工作流里点击 ComfyUI 底部的 Rhino 按钮，会在画布中央插入一个 Batch Images 节点和全部 Rhino 图片节点。插件不会创建或假设任何生图节点，把 Batch Images 的输出连到你使用的任意模型或 API 节点即可。

工作流中已有多个 Batch Images 时，先选中要接入的那一个，再点击 Rhino 按钮。

更换产品、视角、选择或材质后不会自动生成。再次点击“更新到 ComfyUI”即可发布新的输入图片。

普通场景模式保持原来的 9 张控制图，并默认开启“普通场景固定旧版宽屏比例（1024 × 589）”。调整 Rhino 窗口、打开侧边栏或移动插件窗口都不会再把输出变成正方形；插件会对所有控制图使用同一个相机子视锥，Perspective 保持一致。只有开启 Wallpaper 产品佩戴模式时，才会使用参考底图、placement、scale-lock、局部放大和 inpaint 规则。

## Wallpaper 产品佩戴

1. 使用 Rhino 的 `Wallpaper` 命令，把人物、手部或颈部参考照片放入当前视口。
2. 移动产品并调整相机，使 CAD 产品对准实际佩戴位置和透视。
3. 选中产品并开启仅导出选中对象。
4. 开启 Wallpaper 产品佩戴模式，填写佩戴关系要求。
5. 导出并更新到 ComfyUI。

全画幅 `reference` 和 `placement` 决定最终构图、位置与尺度。只有 `reference` 可以决定最终画面的彩色或黑白模式、曝光、对比度、明暗层次、肤色和背景，其他技术控制图不能让结果染色或变灰。`scale_lock.png` 在完整画幅中标记产品的精确轮廓、外接框和中心；其底图现在保持 reference 的原始明暗，其中紫色轮廓、矩形框和十字仅是不可见的测量信息，绝不能出现在最终成图中。自动提示词会在图片职责说明的开头和结尾重复外观锁定与清理要求。放大的 `*_detail` 图片只提供造型与材质细节，不能改变最终产品的大小和位置。

保持“按 Wallpaper 原图比例输出”开启时，Portrait 参考图会直接生成竖幅结果。插件会先以足够的中间分辨率捕获完整 Rhino 视口，再裁出对应的相机子视锥，并把同一竖幅画布用于 reference、placement、rendered、全部几何控制图及蒙版；不会拉伸或旋转照片。

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
| `placement.png` | 产品在参考图上的全画幅位置 |
| `scale_lock.png` | 仅用于全画幅尺度与位置测量，不作为最终外观 |
| `product_mask.png` | 产品在完整画幅中的精确轮廓 |
| `inpaint_mask.png` | 平滑的允许编辑邻域 |
| `tryon.json` | 位置边界、相机、图片职责和约束信息 |

同一次导出的基础控制图使用完全相同的相机和透视。Portrait 输出和局部细节图都使用从原视角裁切出的相机子视锥，不改变相机位置或镜头，因此 Perspective 保持一致。

Material ID 现在使用黑色背景，并严格跟随 Rhino 图层显示颜色。插件会把完全相同的 HEX 编码写进材质映射 Prompt。相邻部件不容易区分时，可以点击“一键分配高对比图层颜色”。正在使用的图层必须采用不同的非黑色颜色，避免材质区域含混或消失。

## 构建与测试

```powershell
.\build.ps1
.\build.ps1 -Tests
node .\tests\sync-core.test.mjs
```

构建脚本使用已安装的 Rhino 8 `RhinoCommon.dll`。在 PowerShell 7 中使用其自带的 Roslyn 编译器；在 Windows PowerShell 5.1 中自动改用 Rhino 8 自带的 Roslyn，因此无需另装 PowerShell 7。输出文件为 `dist/RhinoAI.rhp`。

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

- 图片只发送到面板中配置的 ComfyUI 地址。
- 插件不读取或传输 API Key。
- 导出图片、本机设置和测试输出不会提交到 Git。
- Nano Banana 等参考图模型仍属于生成式模型。Scale Lock 和图片职责能提高一致性，但不等于 ControlNet 的数学像素锁定。
- 当前几何控制图把透明表面视为不透明；不会把贴图、AO、roughness、metalness、displacement、剖切平面或未烘焙 Grasshopper 预览作为原生渲染通道导出。

参考资料：[RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/) 与 [ComfyUI 服务端接口](https://docs.comfy.org/development/comfyui-server/comms_routes)。
