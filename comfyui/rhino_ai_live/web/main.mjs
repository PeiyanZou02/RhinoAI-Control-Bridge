import { app } from "../../scripts/app.js";
import { api } from "../../scripts/api.js";
import { validateManifest, bindBatch, updateGraph, findUnfilledNanoBatch, batchConnected, channelsFor, seedPrompt, migrateBatchPrompt } from "./sync-core.mjs?v=20";

let latest=null, polling=false, statusButton=null, lastReport="";
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
    if(!response.ok){showStatus("Rhino：等待图片 · 点击接入 Batch");return;}
    const data=await response.json();if(!validateManifest(data))throw new Error("同步清单格式不正确");
    latest=data;if(!app.graph)return;
    for(const batch of app.graph._nodes.filter(n=>n.type==="BatchImagesNode"&&n.properties?.rhino_ai_live))migrateBatchPrompt(app.graph,batch);
    const candidate=findUnfilledNanoBatch(app.graph);
    if(candidate){await bindBatch(app.graph,candidate,data,makeNode);seedPrompt(app.graph,candidate,data);}
    await updateGraph(app.graph,data,makeNode,setImage);
    const batches=app.graph._nodes.filter(n=>n.type==="BatchImagesNode"&&n.properties?.rhino_ai_live&&batchConnected(app.graph,n,data));
    const count=batches.length*channelsFor(data).length;
    const overLimit=batches.some(n=>(n.properties.rhino_ai_channels?.length||0)>14&&(n.outputs?.[0]?.links||[]).some(id=>{
      const link=app.graph.links?.get?.(id)??app.graph.links?.[id];return app.graph.getNodeById(link?.target_id)?.type==="GeminiImage2Node";
    }));
    showStatus(count?`Rhino：${count} 张图片已接入 Batch · ${overLimit?"Nano Banana 单次最多 14 张，请调整模型输入":"手动 Run"}`:"Rhino：点击接入当前 Batch Images",overLimit);
    await report(count?"bound":"ready",{revision:data.revision,batches:batches.map(n=>n.id),images:count});
  } catch(error) {showStatus("Rhino 同步失败："+error.message,true);await report("error",{message:error.message});}
  finally {polling=false;}
}
function showStatus(text,error=false) {
  if(statusButton){statusButton.textContent=text;statusButton.style.borderColor=error?"#e56767":"#42b99d";}
}
async function report(state,detail={}) {
  const payload={schema:"rhino-ai-frontend/1",version:20,state,...detail};
  const serialized=JSON.stringify(payload);if(serialized===lastReport)return;
  try {const result=await api.fetchApi("/userdata/rhino_ai_frontend_status.json?overwrite=true",{method:"POST",headers:{"Content-Type":"application/json"},body:serialized});if(result.ok)lastReport=serialized;}catch{}
}
async function connectCurrent() {
  await poll();if(!latest){alert("请先在 Rhino 点击“更新到 ComfyUI”。");return;}
  const selected=Object.values(app.canvas?.selected_nodes||{}).filter(n=>n.type==="BatchImagesNode");
  const candidates=selected.length?selected:app.graph._nodes.filter(n=>n.type==="BatchImagesNode");
  if(candidates.length!==1){alert("请先选中要接入的一个 Batch Images 节点。");return;}
  const fresh=!candidates[0].properties?.rhino_ai_live;
  await bindBatch(app.graph,candidates[0],latest,makeNode);if(fresh)seedPrompt(app.graph,candidates[0],latest);await poll();
}
if(!globalThis[Symbol.for("RhinoAI.Live.ImagesAndRoles.v20")]) {
globalThis[Symbol.for("RhinoAI.Live.ImagesAndRoles.v20")]=true;
app.registerExtension({
  name:"RhinoAI.Live.ImagesOnly",
  setup(){
    statusButton=document.getElementById("rhino-ai-sync-status")||document.createElement("button");statusButton.id="rhino-ai-sync-status";statusButton.type="button";
    statusButton.style.cssText="position:fixed;bottom:24px;left:360px;z-index:1000;padding:10px 16px;border:1px solid #42b99d;border-radius:8px;background:#192725;color:#eef7f4;font:13px system-ui;cursor:pointer;max-width:60vw";
    statusButton.title="同步 Batch 图片和对应的输入职责说明，不生成；保留你自己写的 prompt。";
    statusButton.addEventListener("click",()=>connectCurrent().catch(e=>showStatus(e.message,true)));document.body.append(statusButton);
    showStatus("Rhino：同步扩展已加载");report("loaded");setInterval(poll,2000);poll();
  },
  async afterConfigureGraph(){await poll();},
  async beforeRegisterNodeDef(nodeType,nodeData) {
    if(nodeData.name!=="BatchImagesNode")return;
    const previous=nodeType.prototype.getExtraMenuOptions;
    nodeType.prototype.getExtraMenuOptions=function(_,options) {
      previous?.apply(this,arguments);
      options.push(null,{content:"Rhino · 将此 Batch 的输入换成 Rhino 图片（手动生成）",callback:async()=>{
        await poll();if(!latest){alert("请先在 Rhino 中点击“更新到 ComfyUI”。");return;}
        const fresh=!this.properties?.rhino_ai_live;
        await bindBatch(app.graph,this,latest,makeNode);if(fresh)seedPrompt(app.graph,this,latest);await poll();
      }},{content:"Rhino · 停止此 Batch 自动更新",callback:()=>{
        this.properties.rhino_ai_live=false;
        for(const node of app.graph._nodes)if(node.properties?.rhino_ai_batch===String(this.id)) {
          node.title="Rhino snapshot: "+node.properties.rhino_ai_channel;delete node.properties.rhino_ai_channel;delete node.properties.rhino_ai_batch;
        }
      }});
    };
  }
});
}
