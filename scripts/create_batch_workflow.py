"""Create a native UI workflow from the installed Nano Banana Pro template; never run it."""
import argparse
import copy
import json
from pathlib import Path
from urllib.request import Request, urlopen

ORDER = ["reference", "placement", "shape_lock", "rendered", "basecolor", "color_code", "depth", "depth_inverse", "lineart", "edges", "silhouette", "normal", "mask", "material_id", "object_id", "product_mask", "inpaint_mask", "occlusion_mask"]

def make_workflow(template, manifest):
    graph = copy.deepcopy(template)
    images = {k:v for k,v in manifest["images"].items() if k not in {"object_id", "color_code", "basecolor", "lineart"}}
    channels = [name for name in ORDER if name in images] + sorted(set(images) - set(ORDER))
    batch = next(n for n in graph["nodes"] if n["type"] == "BatchImagesNode")
    generator = next(n for n in graph["nodes"] if n["type"] == "GeminiImage2Node")
    original = next(n for n in graph["nodes"] if n["type"] == "LoadImage")
    graph["nodes"] = [n for n in graph["nodes"] if n["type"] != "LoadImage"]
    graph["links"] = [link for link in graph["links"] if link[3] != batch["id"]]
    max_id = max(n["id"] for n in graph["nodes"])
    max_link = max(link[0] for link in graph["links"])
    batch["inputs"] = []
    batch["pos"] = [440, 200]
    batch["properties"].update(rhino_ai_live=True, rhino_ai_channels=channels)
    batch["title"] = "Batch Images · Rhino 自动更新"
    for i, channel in enumerate(channels):
        loader = copy.deepcopy(original)
        loader.update(id=max_id+i+1, title="RHINO:"+channel, pos=[20, 40+i*340], size=[300, 300])
        loader["widgets_values"] = [images[channel], "image"]
        loader["properties"].update(rhino_ai_batch=str(batch["id"]), rhino_ai_channel=channel)
        link_id = max_link+i+1
        loader["outputs"][0]["links"] = [link_id]
        loader["outputs"][1]["links"] = None
        graph["nodes"].append(loader)
        batch["inputs"].append({"label":f"image{i}", "name":f"images.image{i}", "type":"IMAGE", "link":link_id})
        graph["links"].append([link_id,loader["id"],0,batch["id"],i,"IMAGE"])
    batch["inputs"].append({"label":f"image{len(channels)}", "name":f"images.image{len(channels)}", "type":"IMAGE", "shape":7, "link":None})
    generator["pos"] = [770, 160]
    color_materials = manifest.get("prompts", {}).get("color_materials", "").strip()
    generator["widgets_values"][0] = color_materials
    for n in graph["nodes"]:
        if n["type"] == "SaveImage":
            n["pos"] = [1250, 160]
            n["widgets_values"] = ["RhinoAI/manual_render"]
    graph.update(last_node_id=max_id+len(channels), last_link_id=max_link+len(channels), groups=[])
    graph.setdefault("extra", {})["ds"] = {"scale":0.7, "offset":[40, 40]}
    return graph

if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--server", default="http://127.0.0.1:8000")
    parser.add_argument("--template", required=True)
    parser.add_argument("--output", required=True)
    args=parser.parse_args()
    with urlopen(args.server+"/userdata/rhino_ai_latest.json") as response:
        manifest=json.load(response)
    workflow=make_workflow(json.loads(Path(args.template).read_text(encoding="utf-8-sig")),manifest)
    encoded=json.dumps(workflow,ensure_ascii=False,indent=2).encode("utf-8")
    Path(args.output).write_bytes(encoded)
    request=Request(args.server+"/userdata/workflows%2FRhinoAI_Nano_Banana_Live.json?overwrite=true",data=encoded,headers={"Content-Type":"application/json"},method="POST")
    with urlopen(request) as response:
        print("Saved ComfyUI workflow:", response.status, "RhinoAI_Nano_Banana_Live.json")
    print("No generation submitted.")
