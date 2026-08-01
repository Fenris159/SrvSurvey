"""Generate shared route/body-type artwork as SVG source and runtime PNG assets.

The visual language adapts the body rendering used by RavenColonialWeb. The
runtime PNGs are generated from the same geometry because Avalonia's base image
loader does not include an SVG decoder.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw


VIEW_SIZE = 76
PNG_SIZE = 152
OVERSAMPLE = 4

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = ROOT / "src" / "SrvSurvey.Desktop" / "Assets" / "Bodies"


def circle_svg(radius: int, fill: str, stroke: str) -> str:
    return (
        f'<circle cx="38" cy="38" r="{radius}" fill="{fill}" '
        f'stroke="{stroke}" stroke-width="2" />'
    )


ASSETS: dict[str, str] = {
    "black-hole": circle_svg(10, "#17001A", "#2E2E2E"),
    "neutron-star": circle_svg(10, "#B9FFFF", "#FFFFFF"),
    "white-dwarf": circle_svg(10, "#FFFFFF", "#A8B3BF"),
    "star": circle_svg(30, "#FCED69", "#FBFF00"),
    "gas-giant": circle_svg(22, "#C984A9", "#784A67"),
    "water-giant": circle_svg(18, "#2784D9", "#0A3A70"),
    "water-world": circle_svg(15, "#1F8FE5", "#073B73"),
    "earth-like-world": circle_svg(15, "#54B868", "#1F6530"),
    "ammonia-world": circle_svg(15, "#C98225", "#FFF4DE"),
    "high-metal-content": circle_svg(14, "#9A958C", "#5C4939"),
    "metal-rich": circle_svg(13, "#8C5A3C", "#4A2A1D"),
    "rocky-body": circle_svg(12, "#5B3A2A", "#2D1B13"),
    "rocky-ice-body": circle_svg(12, "#71859B", "#BDD7E8"),
    "icy-body": circle_svg(7, "#A9E7F2", "#E6FAFF"),
    "asteroid-cluster": (
        '<ellipse cx="29" cy="39" rx="8" ry="4" fill="#80766B" '
        'stroke="#B6ADA3" stroke-width="2" transform="rotate(-22 29 39)" />'
        '<ellipse cx="40" cy="32" rx="7" ry="4" fill="#6F675F" '
        'stroke="#B6ADA3" stroke-width="2" transform="rotate(18 40 32)" />'
        '<ellipse cx="47" cy="44" rx="6" ry="3.5" fill="#91877D" '
        'stroke="#C8C0B8" stroke-width="2" transform="rotate(-8 47 44)" />'
    ),
    "barycentre": (
        '<path d="M16 38 H31 M45 38 H60 M38 16 V31 M38 45 V60" '
        'stroke="#AAB2BD" stroke-width="3" stroke-linecap="round" />'
        '<circle cx="38" cy="38" r="7" fill="none" stroke="#E1E6EC" '
        'stroke-width="2" />'
    ),
    "unknown": circle_svg(30, "#343A40", "#747E88"),
}


def svg_document(content: str) -> str:
    return (
        '<?xml version="1.0" encoding="UTF-8"?>\n'
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 76 76">\n'
        f'  {content}\n'
        '</svg>\n'
    )


def scaled(value: float) -> int:
    return round(value * OVERSAMPLE * PNG_SIZE / VIEW_SIZE)


def draw_circle(
    draw: ImageDraw.ImageDraw,
    radius: float,
    fill: str,
    stroke: str,
) -> None:
    center = scaled(38)
    r = scaled(radius)
    draw.ellipse(
        (center - r, center - r, center + r, center + r),
        fill=fill,
        outline=stroke,
        width=scaled(2),
    )


def draw_asset(name: str) -> Image.Image:
    size = PNG_SIZE * OVERSAMPLE
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)

    circles = {
        "black-hole": (10, "#17001A", "#2E2E2E"),
        "neutron-star": (10, "#B9FFFF", "#FFFFFF"),
        "white-dwarf": (10, "#FFFFFF", "#A8B3BF"),
        "star": (30, "#FCED69", "#FBFF00"),
        "gas-giant": (22, "#C984A9", "#784A67"),
        "water-giant": (18, "#2784D9", "#0A3A70"),
        "water-world": (15, "#1F8FE5", "#073B73"),
        "earth-like-world": (15, "#54B868", "#1F6530"),
        "ammonia-world": (15, "#C98225", "#FFF4DE"),
        "high-metal-content": (14, "#9A958C", "#5C4939"),
        "metal-rich": (13, "#8C5A3C", "#4A2A1D"),
        "rocky-body": (12, "#5B3A2A", "#2D1B13"),
        "rocky-ice-body": (12, "#71859B", "#BDD7E8"),
        "icy-body": (7, "#A9E7F2", "#E6FAFF"),
        "unknown": (30, "#343A40", "#747E88"),
    }
    if name in circles:
        draw_circle(draw, *circles[name])
    elif name == "asteroid-cluster":
        # Render three rotated fragments on temporary transparent layers.
        fragments = [
            (29, 39, 8, 4, -22, "#80766B", "#B6ADA3"),
            (40, 32, 7, 4, 18, "#6F675F", "#B6ADA3"),
            (47, 44, 6, 3.5, -8, "#91877D", "#C8C0B8"),
        ]
        for cx, cy, rx, ry, angle, fill, stroke in fragments:
            layer = Image.new("RGBA", image.size, (0, 0, 0, 0))
            layer_draw = ImageDraw.Draw(layer)
            layer_draw.ellipse(
                (
                    scaled(cx - rx),
                    scaled(cy - ry),
                    scaled(cx + rx),
                    scaled(cy + ry),
                ),
                fill=fill,
                outline=stroke,
                width=scaled(2),
            )
            layer = layer.rotate(angle, center=(scaled(cx), scaled(cy)))
            image.alpha_composite(layer)
    elif name == "barycentre":
        width = scaled(3)
        line = "#AAB2BD"
        for start, end in [
            ((16, 38), (31, 38)),
            ((45, 38), (60, 38)),
            ((38, 16), (38, 31)),
            ((38, 45), (38, 60)),
        ]:
            draw.line(
                (scaled(start[0]), scaled(start[1]), scaled(end[0]), scaled(end[1])),
                fill=line,
                width=width,
            )
        center = scaled(38)
        radius = scaled(7)
        draw.ellipse(
            (center - radius, center - radius, center + radius, center + radius),
            outline="#E1E6EC",
            width=scaled(2),
        )
    else:
        raise ValueError(f"No raster renderer for {name}")

    return image.resize((PNG_SIZE, PNG_SIZE), Image.Resampling.LANCZOS)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for name, content in ASSETS.items():
        (OUTPUT / f"{name}.svg").write_text(
            svg_document(content),
            encoding="utf-8",
            newline="\n",
        )
        draw_asset(name).save(OUTPUT / f"{name}.png", optimize=True)


if __name__ == "__main__":
    main()
