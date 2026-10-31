#!/usr/bin/env python3
"""Generates creature art drafts with the OpenAI Images API (specs/26 section 6, issue #116).

Reads the style and species prompts from prompts.json next to this file and writes
<out>/<species>_<n>.webp. Needs OPENAI_API_KEY in the environment and api.openai.com
on the network allow list. Each image costs money: generate only what you will review.

Usage: tools/art/generate.py --species slime drake --n 3 --out tools/art/drafts
"""
import argparse, base64, json, os, sys, urllib.error, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
API = "https://api.openai.com/v1/images/generations"


def generate(prompt, n, model, quality, size):
    body = json.dumps({"model": model, "prompt": prompt, "n": n, "size": size,
                       "quality": quality, "output_format": "webp", "output_compression": 85}).encode()
    req = urllib.request.Request(API, data=body, headers={
        "Authorization": "Bearer " + os.environ["OPENAI_API_KEY"], "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=300) as r:
            data = json.load(r)
    except urllib.error.HTTPError as e:
        sys.exit(f"generate: HTTP {e.code}: {e.read().decode(errors='replace')[:500]}")
    return [base64.b64decode(d["b64_json"]) for d in data["data"]]


def main():
    cfg = json.load(open(os.path.join(HERE, "prompts.json")))
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--species", nargs="+", choices=sorted(cfg["species"]), required=True)
    ap.add_argument("--n", type=int, default=3, help="candidates per species")
    ap.add_argument("--out", default=os.path.join(HERE, "drafts"))
    ap.add_argument("--model", default="gpt-image-2")
    ap.add_argument("--quality", default="medium", choices=["low", "medium", "high"])
    ap.add_argument("--size", default="1024x1024")
    a = ap.parse_args()
    if not os.environ.get("OPENAI_API_KEY"):
        sys.exit("generate: OPENAI_API_KEY is not set (see issue #116)")
    os.makedirs(a.out, exist_ok=True)
    for sp in a.species:
        prompt = cfg["style"] + "\n\nCreature: " + cfg["species"][sp]["prompt"]
        for i, img in enumerate(generate(prompt, a.n, a.model, a.quality, a.size), 1):
            path = os.path.join(a.out, f"{sp}_{i}.webp")
            open(path, "wb").write(img)
            print(path, len(img) // 1024, "KB")


if __name__ == "__main__":
    main()
