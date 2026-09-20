// Show load failures before importing ComfyUI's shims or our graph module.
// This entry point is intentionally dependency-free.
const badge=document.getElementById("rhino-ai-sync-status")||document.createElement("button");
badge.id="rhino-ai-sync-status";badge.type="button";
badge.style.cssText="position:fixed;bottom:24px;left:360px;z-index:10000;padding:10px 16px;border:1px solid #42b99d;border-radius:8px;background:#192725;color:#eef7f4;font:13px system-ui;max-width:60vw";
badge.textContent="Rhino：正在加载同步扩展…";
document.body.append(badge);
const base=new URL("../../",import.meta.url);
async function diagnostic(state,message) {
  try {await fetch(new URL("api/userdata/rhino_ai_frontend_status.json?overwrite=true",base),{
    method:"POST",headers:{"Content-Type":"application/json","Comfy-User":"default"},
    body:JSON.stringify({schema:"rhino-ai-frontend/1",version:21,state,message})
  });}catch{}
}
await diagnostic("bootstrap","Rhino frontend module entry point reached");
try {await import("./main.mjs?v=21");}
catch(error) {
  badge.textContent="Rhino 扩展加载失败："+error.message;badge.style.borderColor="#e56767";
  await diagnostic("import_error",error.message);
  console.error("Rhino AI extension failed to import",error);
}
