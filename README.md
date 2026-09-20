# Rhino AI Control Bridge

[中文说明](README.zh-CN.md)

Rhino AI Control Bridge is a local Rhino 8 plug-in for exporting the active CAD view as a compact set of AI image-generation controls and synchronizing them with a selected **Batch Images** node in ComfyUI. It is designed for product visualization, jewelry try-on, and workflows that need the generated image to follow Rhino geometry, material regions, camera perspective, and placement.

The bridge updates images and prompt metadata only. It never presses Run, queues a prompt, or sends a generation request. Generation remains under the user's control in ComfyUI.

## Features

- Exports depth, inverse depth, camera-space normal, edges, silhouette, foreground mask, rendered view, shape lock, base color, object ID, and layer-based material ID.
- Keeps every control image on the active Rhino viewport's camera, projection, aspect ratio, and frustum.
- Assigns Material ID strictly by Rhino layer so different material regions remain separable.
- Maps layer colors to editable target-material names and syncs concise `#HEX = material` instructions.
- Captures a Rhino Wallpaper as the full-frame base image for jewelry and product placement.
- Matches portrait or landscape Wallpaper source proportions and removes viewport letterboxing while preserving the same camera position, lens, and perspective rays.
- Produces full-frame placement, product, inpaint, occlusion, and scale-lock masks.
- Creates high-resolution local geometry crops while keeping the full-frame placement as the authority for final scale and position.
- Syncs no more than 14 selected inputs for Nano Banana-compatible multi-image workflows.
- Preserves text written by the user in the ComfyUI prompt.
- Uses unique upload names and publishes a batch only after every image upload succeeds.

## Requirements

- Windows 10 or 11
- Rhino 8
- ComfyUI or ComfyUI Desktop running locally
- PowerShell 7 for building from source
- A ComfyUI image model/workflow that accepts multiple image inputs

The plug-in defaults to `http://127.0.0.1:8000`. Change the address in the Rhino panel if your ComfyUI server uses another port, such as `8188`.

## Install

### Rhino plug-in

1. Download `dist/RhinoAI.rhp` or build it from source.
2. Drag the `.rhp` file into Rhino 8, or run `PlugInManager`, choose **Install**, and select the file.
3. Restart Rhino if requested.
4. Run the Rhino command `RhinoAI`.

`RhinoAIExport` performs a direct export with the saved settings without opening the panel.

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

## Basic workflow

1. Start ComfyUI and open a workflow containing **Batch Images** connected to the image model.
2. Open the Rhino model and activate the viewport to export.
3. Run `RhinoAI`.
4. Review the layer-to-material table. Put parts that need separate Material IDs on separate Rhino layers.
5. In ComfyUI, select the intended Batch Images node and bind it with the Rhino action if it is not already bound.
6. Click **Update to ComfyUI** in Rhino.
7. Write or edit the creative prompt in ComfyUI.
8. Click **Run** manually in ComfyUI.

Changing a product, camera, selection, or material mapping does not start generation. Click **Update to ComfyUI** again to publish a new input batch.

Standard scene mode keeps the legacy nine-channel control stack. **Lock standard scene to legacy widescreen (1024 × 589)** is enabled by default, so resizing Rhino, opening a side panel, or changing the plug-in window cannot turn the output square. The plug-in crops the same camera frustum consistently across every control image, preserving perspective. Wallpaper placement, reference-photo, scale-lock, detail-crop, and inpaint instructions are applied only when Wallpaper product mode is enabled.

## Wallpaper product placement

1. Use Rhino's `Wallpaper` command to place the person, hand, neck, or product reference in the active viewport.
2. Position the CAD product over the intended attachment point and match the camera perspective.
3. Select the product and enable selected-object export.
4. Enable Wallpaper product mode and enter the placement instruction.
5. Export and update ComfyUI.

The synchronized batch uses the full-frame reference and placement images as the authority for composition. The reference image is the sole authority for color or monochrome mode, exposure, contrast, tonal range, skin tone, and background. Technical controls must not tint or darken the result. `scale_lock.png` marks the exact product silhouette, bounding box, and center in that frame. Its background now preserves the reference tones; its magenta silhouette, rectangle, and crosshair are invisible measurement metadata and must never appear in the finished image. The generated prompt repeats these appearance and cleanup rules before and after the image-role instructions. Enlarged `*_detail` images provide geometry and material detail only; they must not change the final product's scale or position.

Keep **Match Wallpaper source aspect** enabled to make a portrait reference produce a portrait output. The plug-in captures the complete Rhino view at a sufficient intermediate resolution, crops the matching camera sub-frustum, and applies that same portrait canvas to the reference, placement, rendered view, geometry controls, and masks. It does not stretch or rotate the photograph.

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
| `placement.png` | Product placement over the reference frame |
| `scale_lock.png` | Technical full-frame scale/position annotation; never final appearance |
| `product_mask.png` | Exact product silhouette in the full frame |
| `inpaint_mask.png` | Smooth allowed edit neighborhood |
| `tryon.json` | Placement bounds, camera data, roles, and constraints |

All raster controls from a single export share the same base camera and perspective. Portrait output and detail controls use cropped camera sub-frustums derived from the same view, without changing the camera location or lens.

Material IDs follow the Rhino layer display colors exactly, on a black background. The same HEX values are written into the material mapping prompt. Use **Assign high-contrast layer colors** in the plug-in when adjacent parts are hard to distinguish. Visible layers must use unique, non-black colors so their regions remain unambiguous.

## Build and test

```powershell
.\build.ps1
.\build.ps1 -Tests
node .\tests\sync-core.test.mjs
```

The build uses the Rhino 8 `RhinoCommon.dll` already installed on the machine and the Roslyn compiler bundled with PowerShell 7. The output is `dist/RhinoAI.rhp`.

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

- Images are sent only to the ComfyUI server address configured in the panel.
- The plug-in does not read or transmit API keys.
- Exported images and local settings are ignored by Git.
- Nano Banana and other reference-image models are generative systems. Scale Lock and structured image roles improve adherence but do not provide ControlNet-style mathematical pixel locking.
- Transparent materials are treated as opaque by the geometry controls. Texture, AO, roughness, metalness, displacement, clipping planes, and unbaked Grasshopper previews are not exported as native render passes.

References: [RhinoCommon API](https://developer.rhino3d.com/api/rhinocommon/) and [ComfyUI server routes](https://docs.comfy.org/development/comfyui-server/comms_routes).
