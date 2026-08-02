from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw


ICON_SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
CANVAS_SIZE = 1024
TILE_COLOR = (26, 206, 154, 255)


def extract_white_symbol(source: Image.Image) -> Image.Image:
    """Extract the original white radar symbol without changing its proportions."""
    rgba = source.convert("RGBA")
    alpha_bbox = rgba.getchannel("A").getbbox()
    if alpha_bbox is None:
        raise ValueError("源 PNG 没有可见内容")

    left, top, right, bottom = alpha_bbox
    center_x = (left + right - 1) / 2
    center_y = (top + bottom - 1) / 2
    radius = min(right - left, bottom - top) / 2

    pixels = rgba.load()
    mask = Image.new("L", rgba.size, 0)
    mask_pixels = mask.load()

    # The source is a white symbol composited over a #1ACE9A circle. Red has
    # the largest foreground/background distance, so it provides the cleanest
    # estimate of the original symbol's antialiasing coverage. Restricting the
    # sample to the inner circle rejects the source PNG's pale edge pixels.
    safe_radius_squared = (radius * 0.84) ** 2
    background_red = TILE_COLOR[0]
    foreground_red = 254
    red_range = foreground_red - background_red

    for y in range(rgba.height):
        dy_squared = (y - center_y) ** 2
        for x in range(rgba.width):
            if (x - center_x) ** 2 + dy_squared > safe_radius_squared:
                continue

            red, green, blue, source_alpha = pixels[x, y]
            if source_alpha == 0:
                continue

            coverage = (red - background_red) / red_range
            if coverage <= 0.025:
                continue

            coverage = min(1.0, coverage)
            # Reject pixels that are still strongly green. Mixed edge pixels
            # remain intentionally, retaining the source symbol's smooth edge.
            if green - red > 175 * (1.0 - coverage) + 12:
                continue

            mask_pixels[x, y] = round(255 * coverage * source_alpha / 255)

    symbol_bbox = mask.getbbox()
    if symbol_bbox is None:
        raise ValueError("未能从源 PNG 中识别白色图形")

    return mask.crop(symbol_bbox)


def render_icon(symbol_mask: Image.Image, size: int) -> Image.Image:
    icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(icon)
    corner_radius = round(size * 0.215)
    draw.rounded_rectangle(
        (0, 0, size - 1, size - 1),
        radius=corner_radius,
        fill=TILE_COLOR,
    )

    max_width = round(size * 0.80)
    max_height = round(size * 0.80)
    scale = min(max_width / symbol_mask.width, max_height / symbol_mask.height)
    symbol_size = (
        max(1, round(symbol_mask.width * scale)),
        max(1, round(symbol_mask.height * scale)),
    )
    resized_mask = symbol_mask.resize(symbol_size, Image.Resampling.LANCZOS)

    # Optical centering: the source mark has more visual weight below center.
    # Moving it upward by 1% makes the 16px and 32px variants look balanced.
    x = (size - symbol_size[0]) // 2
    y = (size - symbol_size[1]) // 2 - round(size * 0.01)
    white = Image.new("RGBA", symbol_size, (255, 255, 255, 255))
    icon.paste(white, (x, y), resized_mask)
    return icon


def main() -> None:
    parser = argparse.ArgumentParser(description="生成 FileClean 满画布应用图标")
    parser.add_argument("source", type=Path, help="原始透明 PNG")
    parser.add_argument("png", type=Path, help="输出高清 PNG")
    parser.add_argument("ico", type=Path, help="输出多尺寸 ICO")
    args = parser.parse_args()

    source = Image.open(args.source)
    symbol_mask = extract_white_symbol(source)
    icon = render_icon(symbol_mask, CANVAS_SIZE)

    args.png.parent.mkdir(parents=True, exist_ok=True)
    args.ico.parent.mkdir(parents=True, exist_ok=True)
    icon.save(args.png, "PNG", optimize=True)
    icon.save(args.ico, "ICO", sizes=[(size, size) for size in ICON_SIZES])

    print(f"PNG: {args.png} ({icon.width}x{icon.height})")
    print(f"ICO: {args.ico} ({', '.join(map(str, ICON_SIZES))} px)")
    print(f"Symbol mask: {symbol_mask.width}x{symbol_mask.height}")


if __name__ == "__main__":
    main()
