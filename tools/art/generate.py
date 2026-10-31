#!/usr/bin/env python3
"""Generates creature art with the OpenAI Images API (specs/26 section 8, issue #116).

Prompts live in prompts.json next to this file. Needs OPENAI_API_KEY in the environment and
api.openai.com on the network allow list. Every image costs money: generate only what you will review.

  draft   --style hunter --species slime drake --n 3   new candidates, <out>/<style>/<species>_<n>.webp
  apex    --style hunter --apex living_ocean --ref PICK --out FILE   an apex form drawn from a picked image
  sprite  --ref FINAL --out final/sprite/<key>.webp    a battle sprite (transparent background) from an image

Then run export.py to update web/art/.
"""
import argparse, base64, json, os, sys, urllib.error, urllib.request, uuid

HERE = os.path.dirname(os.path.abspath(__file__))
API = "https://api.openai.com/v1/images/"


def call(endpoint, data, content_type):
    req = urllib.request.Request(API + endpoint, data=data, headers={
        "Authorization": "Bearer " + os.environ["OPENAI_API_KEY"], "Content-Type": content_type})
    try:
        with urllib.request.urlopen(req, timeout=600) as r:
            return [base64.b64decode(d["b64_json"]) for d in json.load(r)["data"]]
    except urllib.error.HTTPError as e:
        sys.exit(f"generate: HTTP {e.code}: {e.read().decode(errors='replace')[:500]}")


def generate(prompt, n, a):
    body = {"model": a.model, "prompt": prompt, "n": n, "size": a.size, "quality": a.quality,
            "output_format": "webp", "output_compression": 85}
    return call("generations", json.dumps(body).encode(), "application/json")


def edit(ref, prompt, a, transparent=False):
    """One image drawn from a reference image (multipart form, stdlib only)."""
    fields = {"model": a.model, "prompt": prompt, "size": a.size, "quality": a.quality, "n": "1",
              "output_format": "webp", "output_compression": "92" if transparent else "85"}
    if transparent:
        fields["background"] = "transparent"
    b = uuid.uuid4().hex
    parts = [f'--{b}\r\nContent-Disposition: form-data; name="{k}"\r\n\r\n{v}\r\n'.encode() for k, v in fields.items()]
    mime = "image/webp" if ref.endswith(".webp") else "image/png"
    parts.append(f'--{b}\r\nContent-Disposition: form-data; name="image[]"; filename="{os.path.basename(ref)}"\r\n'
                 f'Content-Type: {mime}\r\n\r\n'.encode() + open(ref, "rb").read() + b"\r\n")
    parts.append(f"--{b}--\r\n".encode())
    return call("edits", b"".join(parts), "multipart/form-data; boundary=" + b)[0]


def save(path, img):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    open(path, "wb").write(img)
    print(path, len(img) // 1024, "KB")


def main():
    cfg = json.load(open(os.path.join(HERE, "prompts.json")))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    d = sub.add_parser("draft")
    d.add_argument("--style", choices=sorted(cfg["styles"]), required=True)
    d.add_argument("--species", nargs="+", choices=sorted(cfg["species"]), required=True)
    d.add_argument("--n", type=int, default=3, help="candidates per species")
    d.add_argument("--out", default=os.path.join(HERE, "drafts"))
    x = sub.add_parser("apex")
    x.add_argument("--style", choices=sorted(cfg["styles"]), required=True)
    x.add_argument("--apex", choices=sorted(cfg["apex"]), required=True)
    x.add_argument("--ref", required=True)
    x.add_argument("--out", required=True)
    s = sub.add_parser("sprite")
    s.add_argument("--ref", required=True)
    s.add_argument("--out", required=True)
    for p in (d, x, s):   # gpt-image-2 cannot make a transparent background, so sprites use gpt-image-1.5
        p.add_argument("--model", default="gpt-image-1.5" if p is s else "gpt-image-2")
        p.add_argument("--quality", default="medium", choices=["low", "medium", "high"])
        p.add_argument("--size", default="1024x1024")
    a = ap.parse_args()
    if not os.environ.get("OPENAI_API_KEY"):
        sys.exit("generate: OPENAI_API_KEY is not set (see issue #116)")
    if a.cmd == "draft":
        style = cfg["styles"][a.style]
        for sp in a.species:
            creature = cfg["species"][sp]["prompt"]
            if not style["details"]:   # the "one unsettling detail" belongs to the abyss style (specs/26 section 7)
                creature = creature.split(" Unsettling detail:")[0]
            prompt = style["prompt"] + "\n\nCreature: " + creature
            for i, img in enumerate(generate(prompt, a.n, a), 1):
                save(os.path.join(a.out, a.style, f"{sp}_{i}.webp"), img)
    elif a.cmd == "apex":
        style, apex = cfg["styles"][a.style], cfg["apex"][a.apex]
        change = apex["prompt"]
        if not style["details"]:
            change = change.replace(" and unsettling detail", "")
        save(a.out, edit(a.ref, cfg["apex_base"] + " " + change + "\n\nStyle: " + style["prompt"], a))
    else:
        save(a.out, edit(a.ref, cfg["sprite"], a, transparent=True))


if __name__ == "__main__":
    main()
