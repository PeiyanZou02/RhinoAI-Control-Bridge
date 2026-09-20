export const ORDER = ["reference", "placement", "scale_lock", "placement_detail", "rendered_detail", "shape_lock_detail", "material_id_detail", "normal_detail", "edges_detail", "depth_detail", "shape_lock", "rendered", "basecolor", "color_code", "depth", "depth_inverse", "lineart", "edges", "silhouette", "normal", "mask", "material_id", "object_id", "product_mask", "inpaint_mask", "occlusion_mask"];
export const EXCLUDED = new Set(["object_id", "color_code", "basecolor", "lineart"]);
export function channelsFor(data) {
  if(data.images.reference&&data.images.shape_lock_detail) {
    const optimized=["reference","placement","scale_lock","shape_lock_detail","material_id_detail","normal_detail","edges_detail","depth_detail","product_mask","inpaint_mask","occlusion_mask"];
    return [...optimized.filter(key=>data.images[key]),...Object.keys(data.images).filter(key=>!ORDER.includes(key)).sort()].filter(key=>!EXCLUDED.has(key));
  }
  return [...ORDER.filter(key=>data.images[key]), ...Object.keys(data.images).filter(key=>!ORDER.includes(key)).sort()].filter(key=>!EXCLUDED.has(key));
}
const settle = () => new Promise(resolve => {
  if(typeof requestAnimationFrame === "function") requestAnimationFrame(()=>requestAnimationFrame(resolve));
  else setTimeout(resolve,0);
});
export function validateManifest(data) {
  if (data?.schema !== "rhino-ai-live/1" || typeof data.revision !== "string" || !data.images) return false;
  if (data.target !== undefined && !validTarget(data.target)) return false;
  return Object.entries(data.images).every(([key, path]) => /^[a-z_0-9]+$/.test(key) && typeof path === "string" && /^rhino_ai\/[a-zA-Z0-9_-]+\/[a-zA-Z0-9_.-]+\.png$/.test(path));
}
function validTarget(target) {
  const name=target?.workflow;
  return typeof name==="string" && typeof target.request==="string" && /^[^\\:*?"<>|]+\.json$/.test(name) && !name.startsWith("/") && !name.split("/").includes("..");
}
// Workflow paths are reported by ComfyUI as "workflows/<name>"; Rhino stores "<name>".
export function workflowName(path) { return String(path||"").replace(/^workflows\//,""); }
export function targetState(data, activePath) {
  if(!data?.target?.workflow) return "follow";
  return workflowName(activePath)===data.target.workflow ? "match" : "mismatch";
}

const ROLE_START = "Use the supplied input images according to the exact roles below.";
const ROLE_END = "Follow these roles and priorities for every generation using this input batch.";
const ROLES = {
  reference:"MANDATORY PRIMARY BASE IMAGE AND SOLE APPEARANCE AUTHORITY; edit directly into this photograph while preserving its subject identity, anatomy, pose, camera, composition, clothing, background and all unedited pixels. Preserve its exact color or monochrome mode, white balance, exposure, brightness, contrast, tonal range and skin tone. Never replace, recolor, dim or brighten the scene, and never output the product by itself",
  placement_detail:"magnified placement close-up; use it to inspect the product-to-subject contact, alignment and local scale, while the full placement image remains authoritative for global position",
  rendered_detail:"magnified local appearance and volume reference; use its close-up curvature and integration cues without changing the global placement",
  shape_lock_detail:"PRIMARY HIGH-RESOLUTION PRODUCT GEOMETRY reference; preserve its fine openings, thin parts, seams, gaps, component count, local silhouette and proportions exactly, while the full placement image controls global scale and position",
  material_id_detail:"HIGH-RESOLUTION MATERIAL REGION AND INTERFACE reference; preserve every local material boundary, adjacency, seam, overlap, occlusion and front-to-back relationship. Never merge regions, swap materials or allow material bleeding",
  normal_detail:"high-resolution local surface-shape, orientation and fine-curvature reference",
  edges_detail:"high-resolution local hole, crease, thin-edge and internal part-boundary reference",
  depth_detail:"high-resolution local front-to-back order and relative depth reference",
  shape_lock:"PRIMARY geometry reference; preserve the exact object count, silhouette, openings, gaps, overlaps, part boundaries, proportions and camera",
  rendered:"secondary surface-volume and curvature reference; do not copy its temporary white material, lighting or background",
  placement:"ABSOLUTE FULL-FRAME authority for final product position, pixel size, rotation and silhouette bounds. Copy the product at exactly the scale and location shown in this complete canvas; do not enlarge, shrink or move it based on any detail image",
  scale_lock:"TECHNICAL MEASUREMENT INPUT ONLY, never an appearance reference. Its magenta silhouette, bounding rectangle and center crosshair encode only the exact final product extrema and center on the complete canvas. Treat every magenta pixel and every thin box/crosshair line as invisible metadata. The final product must touch the same left/right/top/bottom bounds without exceeding them, but the output must contain none of these marks. Reconstruct clean photograph pixels behind the marks from reference and placement",
  depth:"front-to-back order and relative spatial-depth reference",
  depth_inverse:"confirmation of the same depth relationships only",
  edges:"visible crease, hole and internal part-boundary reference",
  silhouette:"exact outer contour and negative-space reference",
  normal:"surface shape, orientation and curvature-direction reference",
  mask:"exact foreground occupancy reference",
  material_id:"MATERIAL REGION AND INTERFACE reference; each flat color identifies one separate Rhino layer/material region. Preserve every exact region boundary, adjacency, contact seam, overlap, occlusion and front-to-back stacking relationship. Use depth, edges and normal to determine which material region is in front at every connection. Never merge regions, swap their materials, let one material bleed across a boundary, erase a seam, or reproduce the flat ID colors in the final image",
  product_mask:"exact product-region and product-boundary reference",
  inpaint_mask:"EDITABLE NEIGHBORHOOD reference; white marks where the old item may be removed and local pixels may be regenerated, while black must be preserved. The white region is NOT the new product silhouette or size: never scale the product to fill it; final product scale and bounds come only from the full-frame placement image",
  occlusion_mask:"occlusion-protection reference; white protected subject pixels must remain unchanged and stay in front where appropriate"
};
export function imageGuide(data, channels=channelsFor(data)) {
  const lines=channels.map((key,index)=>`Image ${index+1} (${key}): ${ROLES[key]||"additional visual reference; use only for the information visibly encoded in this image"}.`);
  if(channels.includes("scale_lock"))lines.unshift("MANDATORY CLEAN FINAL OUTPUT: return a natural finished photograph only. The scale_lock overlay is invisible metadata. Do not copy, retain, stylize, recolor or redraw any magenta/pink/purple silhouette, rectangle, center crosshair, guide line, marker, diagram, label or measurement graphic. Restore clean reference/placement pixels behind every guide mark while keeping the actual product.");
  if(channels.includes("reference"))lines.unshift("MANDATORY REFERENCE APPEARANCE LOCK: reference alone controls the complete frame's color or monochrome mode, white balance, exposure, brightness, contrast, tonal range, skin tone and background. Never average, blend or transfer appearance from scale_lock, placement, masks, depth, normals, edges, shape_lock or material_id; they are technical data only.");
  const materials=String(data.prompts?.color_materials||"").trim();
  const userInstruction=String(data.prompts?.user_prompt||"").trim();
  const placementConstraint=String(data.prompts?.placement_constraint||"").trim();
  if(channels.includes("reference")) {
    lines.push("The reference image is the mandatory base canvas for the final output. Return an edited version of that same scene, keep its framing and background, and integrate the product at the placement shown; never return an isolated product on a new background.");
    lines.push("Global-versus-detail rule: reference and placement control the full-frame composition, final product location and real scale on the subject. The *_detail images are magnified digital crops rendered from the IDENTICAL camera projection, lens, orientation and perspective rays; they are not alternate viewpoints. Use them only for fine geometry, curvature, openings, seams and material interfaces. Never infer a new camera, change perspective, enlarge the product or move it in the final full-frame image. The inpaint mask is only a permissible editing neighborhood for removing the old item and rebuilding nearby pixels; its white area is never a scale, shape or bounding-box reference. Geometry conflict priority: full-frame placement for scale/location, then shape_lock_detail or shape_lock > silhouette and edges > masks > depth and normal > rendered. Material ID images govern material regions only and must never alter geometry. At every material connection, preserve the exact boundary, neighboring-region identity, seam/contact relationship and occlusion order; use depth, edges and normal together to keep the front material in front and the rear material behind.");
  } else {
    lines.push("STANDARD RHINO SCENE MODE: use the complete Rhino camera view and the original scene-control behavior. Preserve the exact full-frame camera, composition, object count, silhouettes, openings, overlaps and relative scale shown by shape_lock and rendered. Use depth, edges, silhouette, normal and mask only as coordinated geometry evidence. Use material_id only to assign the requested materials to its flat-color regions. Do not apply any Wallpaper placement, reference-photo, inpaint, scale_lock or detail-crop rule in this mode.");
  }
  if(placementConstraint)lines.push(placementConstraint);
  if(materials)lines.push("Material mapping for the material_id image:\n"+materials);
  if(userInstruction)lines.push("Rhino user instruction — follow this requested edit exactly:\n"+userInstruction);
  if(channels.includes("scale_lock"))lines.push("FINAL VALIDATION BEFORE OUTPUT: inspect the completed image and remove every technical overlay originating from scale_lock. The finished photograph may contain the real product only, with zero bounding box, crosshair, guide line, measurement mark or colored annotation. Compare every pixel outside the product edit region against reference and restore its original brightness, contrast, tonal range and color exactly.");
  return ROLE_START+"\n"+lines.join("\n")+"\n"+ROLE_END;
}

export function mergePrompt(text, guide) {
  let own=removeManagedGuide(removeImageGuide(text));
  const templatePrompt="Have the suit corresponding to this character strike a casual and fashionable pose";
  if(own.trim()===templatePrompt)own="";
  return guide+(own.trim()?"\n\n"+own.trim():"");
}

export function removeManagedGuide(text) {
  const value=String(text||"");
  const start=value.indexOf(ROLE_START);if(start<0)return value.trim();
  const end=value.indexOf(ROLE_END,start);if(end<0)return value.trim();
  return (value.slice(0,start)+value.slice(end+ROLE_END.length)).replace(/\n{3,}/g,"\n\n").trim();
}

export function removeImageGuide(text) {
  return String(text||"")
    .replace(/\[RHINO MATERIAL & IMAGE GUIDE\][\s\S]*?\[\/RHINO MATERIAL & IMAGE GUIDE\]/g, "")
    .replace(/\[RHINO IMAGES\]([\s\S]*?)\[\/RHINO IMAGES\]/g,(_,body)=>body.split(/\r?\n/).filter(line=>!/^\s*Image\s+\d+\s*:/i.test(line)).join("\n").trim())
    .replace(/\n{3,}/g,"\n\n").trim();
}

export function migrateBatchPrompt(graph,batch) {
  if(batch.properties?.rhino_ai_prompt_format==="user-owned-v10")return false;
  let changed=false;
  for(const linkId of batch.outputs?.[0]?.links || []) {
    const link=linkAt(graph,linkId);const target=link&&graph.getNodeById(link.target_id);
    const prompt=target?.widgets?.find(w=>w.name==="prompt");
    if(!prompt)continue;
    const clean=removeImageGuide(prompt.value);
    if(clean!==String(prompt.value||"")){prompt.value=clean;changed=true;}
  }
  batch.properties={...batch.properties,rhino_ai_prompt_format:"user-owned-v10"};
  if(changed)graph.setDirtyCanvas?.(true,true);
  return changed;
}

// Seed the guide only when a Batch is first connected. Image polling never
// calls this, so every subsequent user edit in ComfyUI remains untouched.
export function syncPromptInfo(graph,batch,data) {
  const channels=channelsFor(data);let changed=false;
  const guide=imageGuide(data,channels);const oldGuide=String(batch.properties?.rhino_ai_managed_guide||"");
  for(const linkId of batch.outputs?.[0]?.links || []) {
    const link=linkAt(graph,linkId);const target=link&&graph.getNodeById(link.target_id);
    const prompt=target?.widgets?.find(w=>w.name==="prompt");
    if(!prompt)continue;
    let own=String(prompt.value||"");
    if(oldGuide&&own.includes(oldGuide))own=own.replace(oldGuide,"");
    own=removeManagedGuide(removeImageGuide(own));
    const materials=String(data.prompts?.color_materials||"").trim();
    if(materials&&own.includes(materials))own=own.replace(materials,"").replace(/\n{3,}/g,"\n\n").trim();
    // v10 seeded bare #HEX = material lines without a managed block. They now
    // belong to this synchronized guide, so remove stale copies before rebuild.
    own=own.split(/\r?\n/).filter(line=>!/^\s*#[0-9a-f]{6}\s*=\s*.+$/i.test(line)).join("\n").replace(/\n{3,}/g,"\n\n").trim();
    const next=mergePrompt(own,guide);
    if(next!==String(prompt.value||"")){prompt.value=next;prompt.callback?.(next);target.onWidgetChanged?.("prompt",next,String(prompt.value||""),prompt);changed=true;}
  }
  batch.properties={...batch.properties,rhino_ai_prompt_seeded:true,rhino_ai_managed_guide:guide,rhino_ai_prompt_format:"roles-v13"};
  if(changed)graph.setDirtyCanvas?.(true,true);
  return changed;
}
export const seedPrompt=syncPromptInfo;

function linkAt(graph, id) { return graph.links?.get?.(id) ?? graph.links?.[id]; }

function placeholder(node) {
  if(node?.type!=="LoadImage")return false;
  const value=String(node.widgets?.find(w=>w.name==="image")?.value || "");
  return !value || /^api_nano_banana_pro_input_image_\d+\.png$/.test(value);
}

// Model-agnostic: any single Batch whose inputs are all placeholders or RHINO loaders.
export function findUnfilledBatch(graph) {
  const candidates=graph._nodes.filter(batch=> {
    if(batch.type!=="BatchImagesNode" || batch.properties?.rhino_ai_live!==undefined)return false;
    const sources=(batch.inputs||[]).filter(i=>i.type==="IMAGE"&&i.link!=null).map(i=>linkAt(graph,i.link)).filter(Boolean).map(l=>graph.getNodeById(l.origin_id));
    return sources.length>0 && sources.every(n=>placeholder(n)||/^RHINO:/i.test(n?.title||""));
  });
  return candidates.length===1?candidates[0]:null;
}

export const findUnfilledNanoBatch=findUnfilledBatch;

// Empty workflow: create only a Batch Images node plus the Rhino loaders.
// No generator is created or assumed; the user wires the output to any model.
export async function insertInputGroup(graph, data, createNode, origin=[0,0]) {
  const batch=createNode("BatchImagesNode");
  if(!batch)throw new Error("BatchImagesNode unavailable");
  batch.pos=[origin[0],origin[1]];graph.add(batch);
  // A new node sets up its auto-grow sockets on the next frame; wiring earlier loses links.
  await settle();
  await bindBatch(graph,batch,data,createNode);
  // Compact three-row grid to the left of the Batch instead of one tall column.
  const loaders=graph._nodes.filter(n=>n.type==="LoadImage"&&n.properties?.rhino_ai_batch===String(batch.id));
  const columns=Math.ceil(loaders.length/3);
  loaders.forEach((loader,i)=>{loader.pos=[origin[0]-380*(columns-Math.floor(i/3)),origin[1]+(i%3)*360];});
  return batch;
}

// Explicitly bind one Batch Images node. Original LoadImage nodes remain on the canvas.
export async function bindBatch(graph, batch, data, createNode) {
  const channels=channelsFor(data);
  const max=batch.comfyDynamic?.autogrow?.images?.max ?? 50;
  if(channels.length>max)throw new Error(`本批有 ${channels.length} 张图，Batch 节点最多支持 ${max} 张；未截断图片。`);
  const reusable=(batch.inputs||[]).filter(i=>i.type==="IMAGE"&&i.link!=null).map(i=>linkAt(graph,i.link)).filter(Boolean).map(l=>graph.getNodeById(l.origin_id)).filter(n=>placeholder(n)&&!n.properties?.rhino_ai_channel&&(n.outputs?.[0]?.links||[]).length===1);
  const previous=(batch.inputs || []).map((input,index)=>({input,index})).filter(x=>x.input.type==="IMAGE").reverse();
  // Comfy's autogrow compacts slots on the NEXT animation frame. Reconnecting
  // in the same turn lets queued disconnect handlers erase the new links.
  for(const {index} of previous) {
    if(batch.inputs[index]?.link!=null){batch.disconnectInput(index);await settle();}
  }
  for(let i=0;i<channels.length;i++) {
    const key=channels[i];
    let loader=graph._nodes.find(n=>n.type==="LoadImage"&&n.properties?.rhino_ai_batch===String(batch.id)&&n.properties?.rhino_ai_channel===key);
    if(!loader && reusable.length) {
      loader=reusable.shift();loader.properties={...loader.properties,rhino_ai_original_image:loader.widgets?.find(w=>w.name==="image")?.value,rhino_ai_batch:String(batch.id),rhino_ai_channel:key};loader.title="RHINO:"+key;
    }
    if(!loader) {
      loader=createNode("LoadImage");
      if(!loader)throw new Error("LoadImage node unavailable");
      loader.title="RHINO:"+key;loader.properties={...loader.properties,rhino_ai_batch:String(batch.id),rhino_ai_channel:key};
      loader.pos=[batch.pos[0]-380, batch.pos[1]+i*310];graph.add(loader);
    }
    // BatchImagesNode uses the v3 auto-grow socket names (verified against server schema).
    let slot=batch.inputs.findIndex(input=>input.name===`images.image${i}`);
    if(slot<0){batch.addInput(`images.image${i}`,"IMAGE",{label:`image${i}`});slot=batch.inputs.length-1;}
    loader.connect(0,batch,slot);
  }
  await settle();
  // Remove only our disconnected loaders for the explicitly excluded channels.
  // Nodes reused by another branch retain that branch's connections.
  for(const loader of [...graph._nodes]) {
    if(loader.type==="LoadImage"&&loader.properties?.rhino_ai_batch===String(batch.id)&&EXCLUDED.has(loader.properties.rhino_ai_channel)&&!(loader.outputs||[]).some(o=>o.links?.length))graph.remove(loader);
  }
  batch.properties={...batch.properties,rhino_ai_live:true,rhino_ai_channels:channels};
  if(!batchConnected(graph,batch,data))throw new Error("Batch 接线尚未完整，下一次同步会重试。");
  return channels;
}

export function batchConnected(graph,batch,data) {
  const channels=channelsFor(data);
  const inputs=(batch.inputs||[]).filter(i=>i.type==="IMAGE"&&i.link!=null);
  return inputs.length===channels.length && channels.every((key,i)=>{
    const input=batch.inputs.find(s=>s.name===`images.image${i}`);
    const link=input&&linkAt(graph,input.link);
    const loader=link&&graph.getNodeById(link.origin_id);
    return loader?.properties?.rhino_ai_batch===String(batch.id)&&loader.properties.rhino_ai_channel===key;
  });
}

export async function updateGraph(graph, data, createNode, setImage) {
  if(!validateManifest(data))throw new Error("Invalid Rhino sync manifest");
  let updated=0;
  for(const batch of [...graph._nodes].filter(n=>n.type==="BatchImagesNode"&&n.properties?.rhino_ai_live)) {
    const channels=channelsFor(data);
    if(!batchConnected(graph,batch,data)) await bindBatch(graph,batch,data,createNode);
    syncPromptInfo(graph,batch,data);
  }
  for(const node of graph._nodes) {
    const match=/^RHINO:([a-z_0-9]+)$/i.exec(node.title || "");
    const channel=node.properties?.rhino_ai_channel || match?.[1]?.toLowerCase();
    if(node.type!=="LoadImage"||!channel||EXCLUDED.has(channel))continue;
    const widget=node.widgets?.find(w=>w.name==="image");if(!widget)continue;
    const path=data.images[channel] || ""; // Never leave a stale product reference on a missing channel.
    if(widget.value===path && node.properties?.rhino_ai_revision===data.revision)continue;
    // Follow LoadImage's upload widget path so Vue's value validation and
    // output store both see the image, instead of painting a preview only.
    widget.options ||= {};
    const values=widget.options.values;
    if(Array.isArray(values)&&path&&!values.includes(path))values.push(path);
    else if(!values)widget.options.values=path?[path]:[];
    const oldValue=widget.value;
    widget.value=path;node.properties={...node.properties,rhino_ai_revision:data.revision};
    widget.callback?.(path);
    node.onWidgetChanged?.(widget.name,path,oldValue,widget);
    setImage(node,path,data.revision);updated++;
  }
  // Paint only; do not trigger execution, queue callbacks or replace the user's workflow.
  if(updated)graph.setDirtyCanvas?.(true,true);
  return updated;
}
