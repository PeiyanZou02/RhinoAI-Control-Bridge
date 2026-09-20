import { app } from "../../scripts/app.js";
import { api } from "../../scripts/api.js";
import { validateManifest, bindBatch, updateGraph, findUnfilledBatch, batchConnected, channelsFor, seedPrompt, migrateBatchPrompt, targetState, workflowName, insertInputGroup } from "./sync-core.mjs?v=23";

let latest=null, polling=false, statusButton=null, lastReport="", pendingTarget=null, handledRequest=null, sawManifest=false;
const REQUEST_KEY="rhino_ai_target_request";
try{handledRequest=sessionStorage.getItem(REQUEST_KEY);}catch{}
const workflows=()=>app.extensionManager?.workflow;
const activePath=()=>workflows()?.activeWorkflow?.path;
function markHandled(request) {handledRequest=request;try{sessionStorage.setItem(REQUEST_KEY,request);}catch{}}
// Mirrors the frontend's own workflowService.openWorkflow: the current tab's
// draft is persisted by loadGraphData before the target graph is shown.
async function openTarget(name) {
  const store=workflows();
  if(!store?.getWorkflowByPath||typeof app.loadGraphData!=="function")throw new Error("This ComfyUI version cannot switch workflows automatically. Open it manually: "+name);
  let workflow=store.getWorkflowByPath("workflows/"+name);
  if(!workflow){await store.syncWorkflows?.();workflow=store.getWorkflowByPath("workflows/"+name);}
  if(!workflow)throw new Error("Workflow not found: "+name);
  if(!workflow.isLoaded)await workflow.load();
  await app.loadGraphData(JSON.parse(JSON.stringify(workflow.activeState)),true,true,workflow,{checkForRerouteMigration:false,deferWarnings:true});
}
const makeNode=type=>globalThis.LiteGraph.createNode(type);
function setImage(node,path,revision) {
  node.imgs=[];node.imageIndex=0;
  if(!path)return;
  const slash=path.lastIndexOf("/");
  const query=new URLSearchParams({filename:path.slice(slash+1),subfolder:path.slice(0,slash),type:"input",rhino_revision:revision});
  const image=new Image();image.onload=()=>{if(node.properties.rhino_ai_revision===revision){node.imgs=[image];app.graph.setDirtyCanvas(true,true);}};
  image.src=api.apiURL("/view?"+query);
}
async function poll() {
  if(polling)return;polling=true;
  try {
    const response=await api.fetchApi("/userdata/rhino_ai_latest.json",{cache:"no-store"});
    if(!response.ok){showStatus("Rhino: waiting for images · click to connect a Batch");return;}
    const data=await response.json();if(!validateManifest(data))throw new Error("Invalid sync manifest");
    latest=data;if(!app.graph)return;
    // A target left over from an earlier session must not move the user on startup.
    if(!sawManifest){sawManifest=true;if(handledRequest===null&&data.target)markHandled(data.target.request);}
    pendingTarget=null;
    if(targetState(data,activePath())==="mismatch") {
      const name=data.target.workflow;
      if(data.target.request!==handledRequest){markHandled(data.target.request);showStatus("Rhino: switching to "+name+"…");await openTarget(name);}
      if(targetState(data,activePath())==="mismatch") {
        // The user is looking at another file: leave its graph untouched.
        pendingTarget=name;showStatus(`Rhino: target is ${name} · click to switch`);
        await report("waiting_target",{revision:data.revision,target:name});return;
      }
    }
    for(const batch of app.graph._nodes.filter(n=>n.type==="BatchImagesNode"&&n.properties?.rhino_ai_live))migrateBatchPrompt(app.graph,batch);
    const candidate=findUnfilledBatch(app.graph);
    if(candidate){await bindBatch(app.graph,candidate,data,makeNode);seedPrompt(app.graph,candidate,data);}
    await updateGraph(app.graph,data,makeNode,setImage);
    const batches=app.graph._nodes.filter(n=>n.type==="BatchImagesNode"&&n.properties?.rhino_ai_live&&batchConnected(app.graph,n,data));
    const count=batches.length*channelsFor(data).length;
    const overLimit=batches.some(n=>(n.properties.rhino_ai_channels?.length||0)>14&&(n.outputs?.[0]?.links||[]).some(id=>{
      const link=app.graph.links?.get?.(id)??app.graph.links?.[id];return app.graph.getNodeById(link?.target_id)?.type==="GeminiImage2Node";
    }));
    showStatus(count?`Rhino: ${count} images connected · ${overLimit?"Nano Banana accepts at most 14, reduce the inputs":"Run manually"}`:"Rhino: click to connect Batch Images",overLimit);
    await report(count?"bound":"ready",{revision:data.revision,batches:batches.map(n=>n.id),images:count});
  } catch(error) {showStatus("Rhino sync failed: "+error.message,true);await report("error",{message:error.message});}
  finally {polling=false;}
}
function showStatus(text,error=false) {
  if(statusButton){statusButton.textContent=text;statusButton.style.borderColor=error?"#e56767":"#42b99d";}
}
async function report(state,detail={}) {
  const payload={schema:"rhino-ai-frontend/1",version:26,state,active:workflowName(activePath())||null,open:(workflows()?.openWorkflows||[]).map(w=>workflowName(w.path)),...detail};
  const serialized=JSON.stringify(payload);if(serialized===lastReport)return;
  try {const result=await api.fetchApi("/userdata/rhino_ai_frontend_status.json?overwrite=true",{method:"POST",headers:{"Content-Type":"application/json"},body:serialized});if(result.ok)lastReport=serialized;}catch{}
}
function viewCenter() {
  const ds=app.canvas?.ds,element=app.canvas?.canvas;if(!ds||!element)return [0,0];
  const box=element.getBoundingClientRect();
  return [Math.round(box.width/2/ds.scale-ds.offset[0]),Math.round(box.height/2/ds.scale-ds.offset[1])];
}
async function connectCurrent() {
  await poll();if(!latest){alert("Click Update to ComfyUI in Rhino first.");return;}
  if(pendingTarget){await openTarget(pendingTarget);await poll();return;}
  if(!app.graph._nodes.some(n=>n.type==="BatchImagesNode")) {
    const batch=await insertInputGroup(app.graph,latest,makeNode,viewCenter());
    app.canvas?.selectNode?.(batch);app.graph.setDirtyCanvas(true,true);await poll();return;
  }
  const selected=Object.values(app.canvas?.selected_nodes||{}).filter(n=>n.type==="BatchImagesNode");
  const candidates=selected.length?selected:app.graph._nodes.filter(n=>n.type==="BatchImagesNode");
  if(candidates.length!==1){alert("Select the one Batch Images node to connect first.");return;}
  const fresh=!candidates[0].properties?.rhino_ai_live;
  await bindBatch(app.graph,candidates[0],latest,makeNode);if(fresh)seedPrompt(app.graph,candidates[0],latest);await poll();
}
if(!globalThis[Symbol.for("RhinoAI.Live.ImagesAndRoles.v23")]) {
globalThis[Symbol.for("RhinoAI.Live.ImagesAndRoles.v23")]=true;
app.registerExtension({
  name:"RhinoAI.Live.ImagesOnly",
  setup(){
    statusButton=document.getElementById("rhino-ai-sync-status")||document.createElement("button");statusButton.id="rhino-ai-sync-status";statusButton.type="button";
    statusButton.style.cssText="position:fixed;bottom:24px;left:360px;z-index:1000;padding:10px 16px;border:1px solid #42b99d;border-radius:8px;background:#192725;color:#eef7f4;font:13px system-ui;cursor:pointer;max-width:60vw";
    statusButton.title="Syncs Batch images and their role notes. Never generates. In a workflow without Batch Images, a click inserts one with the Rhino image loaders.";
    statusButton.addEventListener("click",()=>connectCurrent().catch(e=>showStatus(e.message,true)));document.body.append(statusButton);
    showStatus("Rhino: sync extension loaded");report("loaded");setInterval(poll,2000);poll();
  },
  async afterConfigureGraph(){await poll();},
  async beforeRegisterNodeDef(nodeType,nodeData) {
    if(nodeData.name!=="BatchImagesNode")return;
    const previous=nodeType.prototype.getExtraMenuOptions;
    nodeType.prototype.getExtraMenuOptions=function(_,options) {
      previous?.apply(this,arguments);
      options.push(null,{content:"Rhino · Feed this Batch with Rhino images (run manually)",callback:async()=>{
        await poll();if(!latest){alert("Click Update to ComfyUI in Rhino first.");return;}
        const fresh=!this.properties?.rhino_ai_live;
        await bindBatch(app.graph,this,latest,makeNode);if(fresh)seedPrompt(app.graph,this,latest);await poll();
      }},{content:"Rhino · Stop updating this Batch",callback:()=>{
        this.properties.rhino_ai_live=false;
        for(const node of app.graph._nodes)if(node.properties?.rhino_ai_batch===String(this.id)) {
          node.title="Rhino snapshot: "+node.properties.rhino_ai_channel;delete node.properties.rhino_ai_channel;delete node.properties.rhino_ai_batch;
        }
      }});
    };
  }
});
}
