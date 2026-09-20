# Rhino to Comfy

[中文说明](README.zh-CN.md)

Rhino to Comfy is a local Rhino 8 plug-in for exporting the active CAD view as a compact set of AI image-generation controls and synchronizing them with a selected **Batch Images** node in ComfyUI. It is designed for architecture, product and object visualization, background photo blending, and workflows that need the generated image to follow Rhino geometry, material regions, camera perspective, and placement.

Synchronization updates images and prompt metadata only. It never presses Run, queues a prompt, or sends a generation request. Generation happens only when you start it: in ComfyUI, or with **Render ticked views** on the **AI render** page, which renders Rhino named views directly through a vendor API without ComfyUI.

## Features

- Exports depth, inverse depth, camera-space normal, edges, silhouette, foreground mask, rendered view, shape lock, base color, object ID, and layer-based material ID.
- Keeps every control image on the active Rhino viewport's camera, projection, aspect ratio, and frustum.
- Assigns Material ID strictly by Rhino layer so different material regions remain separable.
- Maps layer colors to editable target-material names and syncs concise `#HEX = material` instructions.
- Captures a Rhino Wallpaper as the full-frame base image for blending Rhino objects into a background photograph.
- Matches portrait or landscape Wallpaper source proportions and removes viewport letterboxing while preserving the same camera position, lens, and perspective rays.
- Produces full-frame placement, object, inpaint, occlusion, and scale-lock masks.
- Creates high-resolution local geometry crops while keeping the full-frame placement as the authority for final scale and position.
- Syncs no more than 14 selected inputs for Nano Banana-compatible multi-image workflows.
- Preserves text written by the user in the ComfyUI prompt.
- Uses unique upload names and publishes a batch only after every image upload succeeds.

## Requirements

- Windows 10 or 11
- Rhino 8
- ComfyUI or ComfyUI Desktop running locally
- PowerShell 7 or the built-in Windows PowerShell 5.1 for building from source
- A ComfyUI image model/workflow that accepts multiple image inputs

The plug-in defaults to `http://127.0.0.1:8000`. Change the address in the Rhino panel if your ComfyUI server uses another port, such as `8188`.

## Install

### Rhino plug-in

1. Download `dist/Rhino2Comfy.rhp` or build it from source.
2. Drag the `.rhp` file into Rhino 8, or run `PlugInManager`, choose **Install**, and select the file.
3. Restart Rhino if requested.
4. Run the Rhino command `Rhino2Comfy`.

`Rhino2ComfyExport` performs a direct export with the saved settings without opening the panel.

### ComfyUI synchronization extension

From PowerShell in the repository folder:

```powershell
.\install-comfy-extension.ps1
```

The script installs to `%USERPROFILE%\Documents\ComfyUI` by default. Supply a different ComfyUI root when needed:

```powershell
.\install-comfy-extension.ps1 -ComfyRoot "D:\ComfyUI"
```

Restart ComfyUI after the first installation. When updating only the frontend files, save the workflow and refresh the ComfyUI Desktop canvas with **F5**.

## The panel

Run `Rhino2Comfy` to open the panel. It has five pages and one action bar.

| Page | What it holds |
| --- | --- |
| **Sync** | Target workflow, the live ComfyUI status line, selected-objects export and automatic upload |
| **AI render** | Engine and API key, the named views to render, the control images to send, prompt and render folder |
| **Layer materials** | One row per Rhino layer with its ID color and the target material you type |
| **Background blend** | Blending Rhino objects into the viewport Wallpaper photograph |
| **Export settings** | ComfyUI address, export folder, optional API workflow, output size and the fixed widescreen frame |

**Update to ComfyUI** exports and publishes the images. **Export only** writes the files without contacting ComfyUI. Hover any option for a short explanation.

## Basic workflow

1. Start ComfyUI and open a workflow containing **Batch Images** connected to the image model.
2. Open the Rhino model and activate the viewport to export.
3. Run `Rhino2Comfy`.
4. Review the layer-to-material table. Put parts that need separate Material IDs on separate Rhino layers.
5. On the **Sync** page, pick the ComfyUI file to update in the **Target workflow** list, or keep **Follow the current ComfyUI window**.
6. Click **Update to ComfyUI** in Rhino. With a target selected, ComfyUI switches to that file first.
7. Write or edit the creative prompt in ComfyUI.
8. Click **Run** manually in ComfyUI.

### Choosing the target workflow

The list shows every workflow saved in ComfyUI, newest first. **Refresh** reads it again.

- With a file selected, each **Update to ComfyUI** opens or activates that workflow's tab and then updates its Batch Images. Unsaved edits in the previous tab are kept.
- If you later switch to another file yourself, that file is left untouched. The Rhino button at the bottom of ComfyUI shows the target and switches back on click.
- Automatic sync only uploads images. It never switches the ComfyUI window.
- The status line under the list shows which file ComfyUI is displaying and how many images are connected.

### Connecting a new workflow

In a workflow without a Batch Images node, click the Rhino button at the bottom of ComfyUI. It inserts one Batch Images node and all Rhino image loaders at the centre of the canvas. No image model is created or assumed, so connect the Batch Images output to any model or API node you use.

When a workflow already has several Batch Images nodes, select the intended one before clicking the Rhino button.

Changing the model, camera, selection, or material mapping does not start generation. Click **Update to ComfyUI** again to publish a new input batch.

Standard scene mode keeps the legacy nine-channel control stack. **Lock widescreen frame 1024 × 589** is enabled by default, so resizing Rhino, opening a side panel, or changing the plug-in window cannot turn the output square. The plug-in crops the same camera frustum consistently across every control image, preserving perspective. Wallpaper placement, reference-photo, scale-lock, detail-crop, and inpaint instructions are applied only when Wallpaper product mode is enabled.

## AI render: named views without ComfyUI

ComfyUI's image nodes are wrappers around vendor APIs. With your own API key, the **AI render** page calls the vendor directly and saves the result to a local folder.

1. Save the cameras you need with Rhino's `NamedView` command.
2. On the **AI render** page choose the **Engine**:

| Engine | Request | Key environment variable |
| --- | --- | --- |
| Google Gemini (Nano Banana) | `models/<model>:generateContent` | `GEMINI_API_KEY` |
| OpenAI (GPT Image) | `images/edits` | `OPENAI_API_KEY` |
| Volcengine Doubao (Seedream) | `images/generations` | `ARK_API_KEY` |
| ComfyUI | queues the **API workflow** from Export settings and downloads its saved images | none |

3. Paste the API key, or leave the field empty to use the environment variable. Each engine keeps its own key, model and address. The model list is editable, so newer models can be typed in. Change the API address only for a proxy or a compatible gateway.
4. Tick the named views. **(Current viewport)** renders the view as it is now. **Reload views** reads the list from Rhino again.
5. Tick the control images to send, write the prompt and choose the **Render folder**.
6. Click **Render ticked views**. Each view is restored in the active viewport, exported, sent with the prompt, image roles and layer material mapping, and saved as `<view name>_<time>.png`. Your original view is restored at the end. **Cancel** stops the batch and drops the request in progress. A view that fails is reported and the batch continues.

The exact prompt sent for each view is kept as `ai_prompt.txt` in that view's export folder. Export options such as the longest edge, the locked widescreen frame, selected objects only and Background blend apply to every view. Background blend always sends its own reference, placement and mask images.

For the ComfyUI engine, the API workflow can use `{{positive_prompt}}` for your prompt text and `{{full_prompt}}` for the prompt with image roles and material mapping, next to the existing `RHINO:<channel>` image loaders.

Vendor APIs are billed by the vendor per generated image.

## Background blend

1. Use Rhino's `Wallpaper` command to place the background photograph, such as a site, street, interior or portrait, in the active viewport.
2. Position the Rhino objects over the intended location and match the camera perspective to the photograph.
3. Select the objects to insert and enable **Export selected objects only**.
4. On the **Background blend** page, enable **Blend into the Wallpaper background** and write the blend instructions.
5. Export and update ComfyUI.

The synchronized batch uses the full-frame reference and placement images as the authority for composition. The reference image is the sole authority for color or monochrome mode, exposure, contrast, tonal range, colors, and background. Technical controls must not tint or darken the result. `scale_lock.png` marks the exact object silhouette, bounding box, and center in that frame. Its background now preserves the reference tones; its magenta silhouette, rectangle, and crosshair are invisible measurement metadata and must never appear in the finished image. The generated prompt repeats these appearance and cleanup rules before and after the image-role instructions. Enlarged `*_detail` images provide geometry and material detail only; they must not change the final product's scale or position.

Keep **Match the Wallpaper aspect ratio** enabled to make a portrait reference produce a portrait output. The plug-in captures the complete Rhino view at a sufficient intermediate resolution, crops the matching camera sub-frustum, and applies that same portrait canvas to the reference, placement, rendered view, geometry controls, and masks. It does not stretch or rotate the photograph.

For strict compositing, use `inpaint_mask.png` with a workflow that composites generated pixels back over `reference.png`. A text prompt alone cannot guarantee that every pixel outside the edit region remains unchanged.

## Main exported files

| File | Purpose |
| --- | --- |
| `rendered.png` | Rhino Rendered display using the active camera |
| `shape_lock.png` | High-contrast geometry reference |
| `depth.png` | Near-white, far-black linear camera depth |
| `depth_inverse.png` | Inverted depth with black background |
| `normal.png` | Camera-space surface normals |
| `edges.png` | Geometry edges on black |
| `silhouette.png` | Outer foreground contour |
| `mask.png` | Visible geometry foreground mask |
| `material_id.png` | Stable region colors assigned by Rhino layer |
| `basecolor.png` | Flat material base colors without lighting |
| `reference.png` | Full-frame Wallpaper reference |
| `placement.png` | Object placement over the reference frame |
| `scale_lock.png` | Technical full-frame scale/position annotation; never final appearance |
| `object_mask.png` | Exact object silhouette in the full frame |
| `inpaint_mask.png` | Smooth allowed edit neighborhood |
| `blend.json` | Placement bounds, camera data, roles, and constraints |

All raster controls from a single export share the same base camera and perspective. Portrait output and detail controls use cropped camera sub-frustums derived from the same view, without changing the camera location or lens.

Material IDs follow the Rhino layer display colors exactly, on a black background. The same HEX values are written into the material mapping prompt. Use **Assign contrast colors** in the plug-in when adjacent parts are hard to distinguish. Visible layers must use unique, non-black colors so their regions remain unambiguous.

## Build and test

```powershell
.\build.ps1
.\build.ps1 -Tests
node .\tests\sync-core.test.mjs
```

`AiTests` and `SyncTests` in the test assembly run against fake HTTP handlers, so they need neither Rhino, an API key nor a network connection.

The build uses the Rhino 8 `RhinoCommon.dll` already installed on the machine. PowerShell 7 supplies its bundled Roslyn compiler; Windows PowerShell 5.1 falls back to the Roslyn copy shipped with Rhino 8, so PowerShell 7 is optional. The output is `dist/Rhino2Comfy.rhp`.

The synchronization tests verify that the bridge uploads and publishes inputs without calling ComfyUI's prompt or queue endpoints.

## Repository layout

```text
src/        Rhino plug-in source
comfyui/    ComfyUI synchronization extension
workflows/  Example API/workflow JSON
tests/      C# and JavaScript synchronization tests
scripts/    Workflow utility scripts
dist/       Latest prebuilt Rhino plug-in
```

## Privacy and limitations

- Synchronization sends images only to the ComfyUI server address configured in the panel, and never any API key.
- AI render sends the ticked control images and the prompt to the API address shown on that page, and only when you click **Render ticked views**. API keys are stored in the local `settings.json` encrypted with Windows DPAPI for your Windows account, travel only in the request header over HTTPS, and are never written to logs or export folders.
- Exported images and local settings are ignored by Git.
- Nano Banana and other reference-image models are generative systems. Scale Lock and structured image roles improve adherence but do not provide ControlNet-style mathematical pixel locking.
- Transparent materials are treated as opaque by the geometry controls. Texture, AO, roughness, metalness, displacement, clipping planes, and unbaked Grasshopper previews are not exported as native render passes.

References: [RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/) and [ComfyUI server routes](https://docs.comfy.org/development/comfyui-server/comms_routes).
