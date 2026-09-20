import assert from "node:assert/strict";
import { validateManifest, bindBatch, updateGraph, mergePrompt, findUnfilledNanoBatch, channelsFor, batchConnected, imageGuide, seedPrompt, removeImageGuide, migrateBatchPrompt, targetState, workflowName, insertInputGroup, findUnfilledBatch } from "../comfyui/rhino_ai_live/web/sync-core.mjs";

let nextId=10, nextLink=1;
const graph={_nodes:[],links:{},getNodeById(id){return this._nodes.find(n=>n.id===id);},add(node){node.id=nextId++;this._nodes.push(node);},setDirtyCanvas(){},remove(n){this._nodes=this._nodes.filter(x=>x!==n);}};
function node(type){return {type,properties:{},widgets:type==="LoadImage"?[{name:"image",value:"old.png"}]:[],inputs:[],outputs:[{links:[]}],pos:[400,100],addInput(name,type){this.inputs.push({name,type,link:null});},disconnectInput(slot){const input=this.inputs[slot];if(input.link!=null){const link=graph.links[input.link];const source=graph.getNodeById(link.origin_id);source.outputs[0].links=source.outputs[0].links.filter(id=>id!==input.link);delete graph.links[input.link];input.link=null;}},connect(sourceSlot,target,targetSlot){const id=nextLink++;graph.links[id]={origin_id:this.id,target_id:target.id};this.outputs[sourceSlot].links.push(id);target.inputs[targetSlot].link=id;}};}
const batch=node("BatchImagesNode"), generator=node("GeminiImage2Node"), original=node("LoadImage");
graph.add(batch);graph.add(generator);graph.add(original);batch.addInput("images.image0","IMAGE");generator.addInput("images","IMAGE");generator.widgets=[{name:"prompt",value:"My original request"}];original.connect(0,batch,0);batch.connect(0,generator,0);
const data={schema:"rhino-ai-live/1",revision:"v1",images:{edges:"rhino_ai/a/edges.png",depth:"rhino_ai/a/depth.png",reference:"rhino_ai/a/reference.png"},prompts:{positive_prompt:"Gold ring"}};
assert(validateManifest(data));assert(!validateManifest({...data,images:{depth:"../../secret"}}));
await bindBatch(graph,batch,data,node);const drawn=[];await updateGraph(graph,data,node,(n,path)=>drawn.push(path));
assert.equal(seedPrompt(graph,batch,data),false);
assert.equal(batch.properties.rhino_ai_channels.join(","),"reference,depth,edges");
assert.equal(graph.getNodeById(graph.links[batch.inputs[0].link].origin_id).widgets[0].value,data.images.reference);
assert.equal(original.widgets[0].value,"old.png");assert(generator.widgets[0].value.includes("Image 1 (reference):"));assert(generator.widgets[0].value.endsWith("My original request"));
assert.equal(drawn.length,3);assert.equal(graph.getNodeById(graph.links[batch.outputs[0].links[0]].target_id),generator);
const newer={...data,revision:"v2",images:{edges:"rhino_ai/b/edges.png",depth:"rhino_ai/b/depth.png"}};
await updateGraph(graph,newer,node,()=>{});
assert.deepEqual(batch.properties.rhino_ai_channels,["depth","edges"]);
assert.equal(graph.getNodeById(graph.links[batch.inputs[0].link].origin_id).widgets[0].value,newer.images.depth);
assert.equal(graph._nodes.find(n=>n.properties.rhino_ai_channel==="reference").widgets[0].value,"");
generator.widgets[0].value="User changed prompt freely";
await updateGraph(graph,newer,node,()=>{});assert(generator.widgets[0].value.includes("Image 1 (depth):"));assert(generator.widgets[0].value.endsWith("User changed prompt freely"));
assert.equal(seedPrompt(graph,batch,newer),false);assert(generator.widgets[0].value.endsWith("User changed prompt freely"));
assert(!mergePrompt("mine","#AAAAAA = gold").includes("[RHINO IMAGES]"));
assert.equal((generator.widgets[0].value.match(/Use the supplied input images/g)||[]).length,1);
const materialUpdate={...newer,revision:"v3",prompts:{color_materials:"#E63946 = glazed ceramic"}};
await updateGraph(graph,materialUpdate,node,()=>{});
assert(generator.widgets[0].value.includes("#E63946 = glazed ceramic"));assert(generator.widgets[0].value.endsWith("User changed prompt freely"));
const materialUpdate2={...materialUpdate,revision:"v4",prompts:{color_materials:"#E63946 = polished silver"}};
await updateGraph(graph,materialUpdate2,node,()=>{});
assert(!generator.widgets[0].value.includes("glazed ceramic"));assert(generator.widgets[0].value.includes("#E63946 = polished silver"));assert.equal((generator.widgets[0].value.match(/Use the supplied input images/g)||[]).length,1);
console.log("Prompt ownership passed: image roles refresh while the user's own request is preserved, no execution APIs.");

const unfilled=node("BatchImagesNode"),missing1=node("LoadImage"),missing2=node("LoadImage");
graph.add(unfilled);graph.add(missing1);graph.add(missing2);unfilled.addInput("images.image0","IMAGE");unfilled.addInput("images.image1","IMAGE");
missing1.widgets[0].value="api_nano_banana_pro_input_image_1.png";missing2.widgets[0].value="api_nano_banana_pro_input_image_2.png";
missing1.connect(0,unfilled,0);missing2.connect(0,unfilled,1);unfilled.connect(0,generator,0);
assert.equal(findUnfilledNanoBatch(graph),unfilled);
missing2.widgets[0].value="my-real-reference.png";assert.equal(findUnfilledNanoBatch(graph),null);
missing2.widgets[0].value="api_nano_banana_pro_input_image_2.png";
unfilled.properties.rhino_ai_live=false;assert.equal(findUnfilledNanoBatch(graph),null);delete unfilled.properties.rhino_ai_live;
const countBefore=graph._nodes.length;await bindBatch(graph,unfilled,newer,node);await updateGraph(graph,newer,node,()=>{});
assert.equal(graph._nodes.length,countBefore);
assert.equal(missing1.widgets[0].value,newer.images.depth);
assert.equal(missing2.widgets[0].value,newer.images.edges);
assert.equal(findUnfilledNanoBatch(graph),null);
assert(generator.widgets[0].value.endsWith("User changed prompt freely"));
console.log("8 regression checks passed: template auto-bind, missing loader reuse, valid reference protection, stop preference.");

// Reproduce native autogrow's deferred compaction, which used to delete new links.
const nativeDisconnect=unfilled.disconnectInput;
unfilled.disconnectInput=function(slot){
  nativeDisconnect.call(this,slot);
  setTimeout(()=>{
    for(let i=slot;i+1<this.inputs.length;i++)this.inputs[i].link=this.inputs[i+1].link;
    this.inputs[this.inputs.length-1].link=null;
    while(this.inputs.length>2 && this.inputs.at(-1).link==null && this.inputs.at(-2).link==null)this.inputs.pop();
  },0);
};
const allNames=['shape_lock','rendered','depth','depth_inverse','normal','mask','color_code','material_id','object_id','basecolor','lineart','edges','silhouette'];
const all={...data,revision:'all11',images:Object.fromEntries(allNames.map(k=>[k,`rhino_ai/all/${k}.png`]))};
await bindBatch(graph,unfilled,all,node);
assert(batchConnected(graph,unfilled,all));
assert.equal(unfilled.inputs.filter(i=>i.link!=null).length,9);
let widgetCalls=0,changedCalls=0;
for(const loader of graph._nodes.filter(n=>n.properties?.rhino_ai_batch===String(unfilled.id))){
  loader.widgets[0].options={values:[]};loader.widgets[0].callback=()=>widgetCalls++;
  loader.onWidgetChanged=()=>changedCalls++;
}
await updateGraph(graph,all,node,()=>{});
assert.equal(widgetCalls,9);assert.equal(changedCalls,9);
for(const loader of graph._nodes.filter(n=>n.properties?.rhino_ai_batch===String(unfilled.id)))assert(loader.widgets[0].options.values.includes(loader.widgets[0].value));
unfilled.disconnectInput(3);await new Promise(r=>setTimeout(r,5));
assert(!batchConnected(graph,unfilled,all));
await updateGraph(graph,all,node,()=>{});assert(batchConnected(graph,unfilled,all));
const expanded={...all,revision:'all16',images:{...all.images,reference:'rhino_ai/all/reference.png',placement:'rhino_ai/all/placement.png',inpaint_mask:'rhino_ai/all/inpaint_mask.png',occlusion_mask:'rhino_ai/all/occlusion_mask.png',future_channel:'rhino_ai/all/future.png'}};
await updateGraph(graph,expanded,node,()=>{});
assert.equal(channelsFor(expanded).length,14);assert(batchConnected(graph,unfilled,expanded));
assert(imageGuide(expanded,channelsFor(expanded)).includes("Image 1 (reference):"));
assert(imageGuide(expanded,channelsFor(expanded)).includes("Image 14 (future_channel):"));
const wallpaperGuide=imageGuide({...expanded,prompts:{color_materials:'#E63946 = ceramic',user_prompt:'Replace the original earring at the aligned location.'}},channelsFor(expanded));
assert(wallpaperGuide.includes('MANDATORY PRIMARY BASE IMAGE'));assert(wallpaperGuide.includes('never return an isolated object'));assert(wallpaperGuide.includes('Rhino user instruction'));assert(wallpaperGuide.includes('Replace the original earring'));
const detailImages={...expanded.images,scale_lock:'rhino_ai/all/scale_lock.png',placement_detail:'rhino_ai/all/placement_detail.png',rendered_detail:'rhino_ai/all/rendered_detail.png',shape_lock_detail:'rhino_ai/all/shape_lock_detail.png',material_id_detail:'rhino_ai/all/material_id_detail.png',normal_detail:'rhino_ai/all/normal_detail.png',edges_detail:'rhino_ai/all/edges_detail.png',depth_detail:'rhino_ai/all/depth_detail.png',object_mask:'rhino_ai/all/object_mask.png'};
const detailData={...expanded,images:detailImages};
assert.deepEqual(channelsFor(detailData),['reference','placement','scale_lock','shape_lock_detail','material_id_detail','normal_detail','edges_detail','depth_detail','object_mask','inpaint_mask','occlusion_mask','future_channel']);
const detailGuide=imageGuide({...detailData,prompts:{placement_constraint:'FULL-FRAME PLACEMENT LOCK: normalized box=(0.48,0.52)-(0.53,0.60).'}},channelsFor(detailData));assert(detailGuide.includes('IDENTICAL camera projection'));assert(detailGuide.includes('Never infer a new camera'));assert(detailGuide.includes('ABSOLUTE FULL-FRAME authority'));assert(detailGuide.includes('normalized box='));assert(detailGuide.includes('MANDATORY CLEAN FINAL OUTPUT'));assert(detailGuide.includes('MANDATORY REFERENCE APPEARANCE LOCK'));assert(detailGuide.includes('SOLE APPEARANCE AUTHORITY'));assert(detailGuide.includes('Never average, blend or transfer appearance'));assert(detailGuide.includes('remove every technical overlay'));assert(detailGuide.includes('restore its original brightness, contrast, tonal range and color exactly'));assert(detailGuide.includes('zero bounding box, crosshair, guide line'));
assert(detailGuide.includes('EDITABLE NEIGHBORHOOD'));assert(detailGuide.includes('white area is never a scale'));
assert(detailGuide.includes('TECHNICAL MEASUREMENT INPUT ONLY'));assert(detailGuide.includes('same left/right/top/bottom bounds'));
await updateGraph(graph,all,node,()=>{});assert(batchConnected(graph,unfilled,all));
assert.equal(unfilled.inputs.filter(i=>i.link!=null).length,9);
console.log('Full-batch regression passed: 9 retained channels including shape_lock + rendered, deferred autogrow, lost-wire repair, widget notifications, new channels, 14-to-9 shrink.');
const removed=node('LoadImage');graph.add(removed);
removed.properties={rhino_ai_batch:String(unfilled.id),rhino_ai_channel:'object_id'};
unfilled.addInput('images.image9','IMAGE');removed.connect(0,unfilled,unfilled.inputs.length-1);
await updateGraph(graph,all,node,()=>{});
assert(!graph._nodes.includes(removed));assert(batchConnected(graph,unfilled,all));
assert.deepEqual(channelsFor(all),['shape_lock','rendered','depth','depth_inverse','edges','silhouette','normal','mask','material_id']);
const guide=imageGuide({...all,prompts:{positive_prompt:'Use the accompanying color-code image as a guide.\nGuide #4361EE / RGB(67, 97, 238) → layer [Metal] → gold'}},channelsFor(all));
assert(!guide.includes('color-code'));assert(!guide.includes('Guide #'));assert(!guide.includes('[Metal] → gold'));assert(guide.includes('Image 9 (material_id):'));assert(guide.includes('front-to-back stacking relationship'));assert(guide.includes('let one material bleed across a boundary'));
assert(imageGuide(data,['basecolor']).includes('Image 1 (basecolor):'));
assert(imageGuide({...data,prompts:{color_materials:'#E63946 = glazed ceramic'}},['depth']).includes('Material mapping for the material_id image:\n#E63946 = glazed ceramic'));
const manual='My lighting prompt\nColor code: blue = silver';
const migrated=mergePrompt(manual+'\n[RHINO MATERIAL & IMAGE GUIDE]\nOld automatic material and style prompt\n[/RHINO MATERIAL & IMAGE GUIDE]', '#E63946 = ceramic');
assert(migrated.startsWith('#E63946 = ceramic'));assert(!migrated.includes('Old automatic'));assert(migrated.endsWith(manual));
const oldBlock=manual+'\n\n[RHINO IMAGES]\nImage 1: depth\nImage 2: normal\n\n#E63946 = ceramic\n[/RHINO IMAGES]';
assert.equal(removeImageGuide(oldBlock),manual+'\n\n#E63946 = ceramic');
generator.widgets[0].value=oldBlock;delete batch.properties.rhino_ai_prompt_format;
assert(migrateBatchPrompt(graph,batch));assert.equal(generator.widgets[0].value,manual+'\n\n#E63946 = ceramic');
generator.widgets[0].value+='\nuser edit';assert.equal(migrateBatchPrompt(graph,batch),false);assert(generator.widgets[0].value.endsWith('user edit'));
console.log('Channel removal verified: managed node deleted, seven inputs retained, color-guide instructions removed.');

const sceneData={schema:'rhino-ai-live/1',revision:'scene',images:{shape_lock:'rhino_ai/scene/shape_lock.png',rendered:'rhino_ai/scene/rendered.png',depth:'rhino_ai/scene/depth.png',depth_inverse:'rhino_ai/scene/depth_inverse.png',edges:'rhino_ai/scene/edges.png',silhouette:'rhino_ai/scene/silhouette.png',normal:'rhino_ai/scene/normal.png',mask:'rhino_ai/scene/mask.png',material_id:'rhino_ai/scene/material_id.png'},prompts:{color_materials:'#112233 = metal',user_prompt:'',placement_constraint:''},input_profile:'scene'};
const sceneGuide=imageGuide(sceneData,channelsFor(sceneData));assert(sceneGuide.includes('STANDARD RHINO SCENE MODE'));assert(sceneGuide.includes('original scene-control behavior'));assert(sceneGuide.includes('Do not apply any Wallpaper placement'));assert(!sceneGuide.includes('MANDATORY REFERENCE APPEARANCE LOCK'));assert(!sceneGuide.includes('Global-versus-detail rule'));
console.log('Scene-mode isolation passed: legacy Rhino controls remain separate from Wallpaper placement rules.');

// Target workflow selection.
const targeted={...sceneData,target:{workflow:'CHOGA.json',request:'r1'}};
assert(validateManifest(targeted));assert(validateManifest({...sceneData,target:{workflow:'jewelry/ring v2.json',request:'r'}}));
assert(!validateManifest({...sceneData,target:{workflow:'../secret.json',request:'r'}}));
assert(!validateManifest({...sceneData,target:{workflow:'a/../../b.json',request:'r'}}));
assert(!validateManifest({...sceneData,target:{workflow:'sub\\x.json',request:'r'}}));
assert(!validateManifest({...sceneData,target:{workflow:'/abs.json',request:'r'}}));
assert(!validateManifest({...sceneData,target:{workflow:'notjson.txt',request:'r'}}));
assert(!validateManifest({...sceneData,target:{workflow:'CHOGA.json'}}));
assert.equal(workflowName('workflows/CHOGA.json'),'CHOGA.json');
assert.equal(targetState(sceneData,'workflows/anything.json'),'follow');
assert.equal(targetState(targeted,'workflows/CHOGA.json'),'match');
assert.equal(targetState(targeted,'workflows/xxx.json'),'mismatch');
assert.equal(targetState(targeted,undefined),'mismatch');
console.log('Target workflow checks passed: manifest validation rejects traversal, follow/match/mismatch resolved.');

// Empty workflow: one click inserts a model-agnostic Batch + Rhino loaders.
let emptyId=1,emptyLink=1;
const empty={_nodes:[],links:{},getNodeById(id){return this._nodes.find(n=>n.id===id);},add(n){n.id=emptyId++;this._nodes.push(n);},setDirtyCanvas(){},remove(n){this._nodes=this._nodes.filter(x=>x!==n);}};
function emptyNode(type){const n=node(type);n.disconnectInput=function(){};n.connect=function(slot,target,targetSlot){const id=emptyLink++;empty.links[id]={origin_id:this.id,target_id:target.id};this.outputs[slot].links.push(id);target.inputs[targetSlot].link=id;};return n;}
const inserted=await insertInputGroup(empty,sceneData,emptyNode,[500,200]);
assert.equal(inserted.type,'BatchImagesNode');assert.deepEqual(inserted.pos,[500,200]);
assert.equal(empty._nodes.filter(n=>n.type==='LoadImage').length,9);
assert.equal(empty._nodes.length,10);assert(batchConnected(empty,inserted,sceneData));assert.equal(inserted.properties.rhino_ai_live,true);
assert(!empty._nodes.some(n=>n.type==='GeminiImage2Node'));
const painted=[];await updateGraph(empty,sceneData,emptyNode,(n,path)=>painted.push(path));assert.equal(painted.length,9);

// Auto-bind no longer requires a Nano Banana target.
const other={_nodes:[],links:{},getNodeById(id){return this._nodes.find(n=>n.id===id);},add(n){n.id=emptyId++;this._nodes.push(n);}};
function otherNode(type){const n=node(type);n.connect=function(slot,target,targetSlot){const id=emptyLink++;other.links[id]={origin_id:this.id,target_id:target.id};this.outputs[slot].links.push(id);target.inputs[targetSlot].link=id;};return n;}
const ob=otherNode('BatchImagesNode'),ol=otherNode('LoadImage'),og=otherNode('SomeOtherApiNode');other.add(ob);other.add(ol);other.add(og);
ob.addInput('images.image0','IMAGE');og.addInput('images','IMAGE');ol.widgets[0].value='';ol.connect(0,ob,0);ob.connect(0,og,0);
assert.equal(findUnfilledBatch(other),ob);
console.log('Input-group checks passed: empty workflow gets Batch + 9 loaders, no generator assumed, non-Gemini auto-bind.');
