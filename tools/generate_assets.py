import os
import numpy as np
from PIL import Image, ImageDraw

# 6x9 pixel grid for each character:
# 3: Highlight, 2: Bright tone, 1: Mid tone, ' ': Empty

I_cap = [
    '333333',
    '333333',
    '  32  ',
    '  22  ',
    '  22  ',
    '  12  ',
    '  11  ',
    '111111',
    '111111'
]

f_lower = [
    '  3333',
    ' 33333',
    ' 32   ',
    '222222',
    '222222',
    ' 12   ',
    ' 11   ',
    ' 11   ',
    ' 11   '
]

c_lower = [
    '      ',
    '      ',
    '      ',
    ' 2222 ',
    '222222',
    '12    ',
    '11    ',
    '111111',
    ' 1111 '
]

O_cap = [
    ' 3333 ',
    '333333',
    '32  32',
    '22  22',
    '22  22',
    '12  12',
    '11  11',
    '111111',
    ' 1111 '
]

# Color palettes:
# Ifc: buildingSMART Flame Amber / Orange
pal_ifc = {
    0: '#431407',  # Deep shadow
    1: '#C2410C',  # Mid tone
    2: '#EA580C',  # Bright tone
    3: '#FDBA74',  # Highlight
}

# IO: Electric Violet / Purple (UNIQUE for IfcIO!)
pal_io = {
    0: '#2E1065',  # Deep shadow (violet-950)
    1: '#7E22CE',  # Mid tone (purple-700)
    2: '#A855F7',  # Bright tone (purple-500)
    3: '#D8B4FE',  # Highlight (purple-300)
}

def build_svg(width, height, rx, block_size, base_xs, start_y, shadow_offset, letters, title):
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" role="img"><title>{title}</title><rect width="{width}" height="{height}" rx="{rx}" fill="#12161C"/><g shape-rendering="crispEdges">']
    
    # Render in two letter groups: IFC, then IO
    for group_name, letter_indices, pal in [('ifc', [0, 1, 2], pal_ifc), ('io', [3, 4], pal_io)]:
        # Shadow layers (6 extrusion steps)
        shadow_rects = []
        for idx in letter_indices:
            grid = letters[idx]
            base_x = base_xs[idx]
            for step in range(1, 7):
                ox = base_x + step * shadow_offset
                oy = start_y + step * shadow_offset
                for r in range(9):
                    c = 0
                    while c < 6:
                        if grid[r][c] != ' ':
                            start_c = c
                            while c < 6 and grid[r][c] != ' ':
                                c += 1
                            span_w = (c - start_c) * block_size
                            x = ox + start_c * block_size
                            y = oy + r * block_size
                            x_str = f"{x:.1f}".rstrip('0').rstrip('.') if isinstance(x, float) else str(x)
                            y_str = f"{y:.1f}".rstrip('0').rstrip('.') if isinstance(y, float) else str(y)
                            w_str = f"{span_w:.1f}".rstrip('0').rstrip('.') if isinstance(span_w, float) else str(span_w)
                            h_str = f"{block_size:.1f}".rstrip('0').rstrip('.') if isinstance(block_size, float) else str(block_size)
                            shadow_rects.append(f"M{x_str} {y_str}h{w_str}v{h_str}h-{w_str}z")
                        else:
                            c += 1
        out.append(f'<path fill="{pal[0]}" d="{"".join(shadow_rects)}"/>')

        for color_val in [3, 2, 1]:
            face_rects = []
            for idx in letter_indices:
                grid = letters[idx]
                base_x = base_xs[idx]
                ox = base_x
                oy = start_y
                for r in range(9):
                    c = 0
                    while c < 6:
                        if grid[r][c] == str(color_val):
                            start_c = c
                            while c < 6 and grid[r][c] == str(color_val):
                                c += 1
                            span_w = (c - start_c) * block_size
                            x = ox + start_c * block_size
                            y = oy + r * block_size
                            x_str = f"{x:.1f}".rstrip('0').rstrip('.') if isinstance(x, float) else str(x)
                            y_str = f"{y:.1f}".rstrip('0').rstrip('.') if isinstance(y, float) else str(y)
                            w_str = f"{span_w:.1f}".rstrip('0').rstrip('.') if isinstance(span_w, float) else str(span_w)
                            h_str = f"{block_size:.1f}".rstrip('0').rstrip('.') if isinstance(block_size, float) else str(block_size)
                            face_rects.append(f"M{x_str} {y_str}h{w_str}v{h_str}h-{w_str}z")
                        else:
                            c += 1
            if face_rects:
                out.append(f'<path fill="{pal[color_val]}" d="{"".join(face_rects)}"/>')

    out.append('</g></svg>')
    return "".join(out)

def render_png(width, height, rx, block_size, base_xs, start_y, shadow_offset, letters, scale=1, alpha_bg=False):
    sw = int(width * scale)
    sh = int(height * scale)
    mode = 'RGBA' if alpha_bg else 'RGB'
    bg = (0, 0, 0, 0) if alpha_bg else '#12161C'
    img = Image.new(mode, (sw, sh), bg)
    draw = ImageDraw.Draw(img)

    if alpha_bg:
        draw.rounded_rectangle([0, 0, sw - 1, sh - 1], radius=int(rx * scale), fill='#12161C')

    # Draw shadow
    for group_name, letter_indices, pal in [('ifc', [0, 1, 2], pal_ifc), ('io', [3, 4], pal_io)]:
        shadow_color = pal[0]
        for idx in letter_indices:
            grid = letters[idx]
            base_x = base_xs[idx]
            for step in range(1, 7):
                ox = (base_x + step * shadow_offset) * scale
                oy = (start_y + step * shadow_offset) * scale
                for r in range(9):
                    for c in range(6):
                        if grid[r][c] != ' ':
                            x1 = int(ox + c * block_size * scale)
                            y1 = int(oy + r * block_size * scale)
                            x2 = int(ox + (c + 1) * block_size * scale - 1)
                            y2 = int(oy + (r + 1) * block_size * scale - 1)
                            draw.rectangle([x1, y1, x2, y2], fill=shadow_color)

    # Draw faces
    for group_name, letter_indices, pal in [('ifc', [0, 1, 2], pal_ifc), ('io', [3, 4], pal_io)]:
        for idx in letter_indices:
            grid = letters[idx]
            base_x = base_xs[idx]
            ox = base_x * scale
            oy = start_y * scale
            for r in range(9):
                for c in range(6):
                    val = grid[r][c]
                    if val != ' ':
                        color = pal[int(val)]
                        x1 = int(ox + c * block_size * scale)
                        y1 = int(oy + r * block_size * scale)
                        x2 = int(ox + (c + 1) * block_size * scale - 1)
                        y2 = int(oy + (r + 1) * block_size * scale - 1)
                        draw.rectangle([x1, y1, x2, y2], fill=color)

    return img

def render_canonical_icon():
    # Remap RvtIO icon directly to ensure exact 100% parity with RvtIO's glyph height, font, and bounds
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    rvt_icon_path = os.environ.get('RVT_ICON_PATH') or os.path.abspath(os.path.join(root, '..', 'RvtIO', 'src', 'RvtIO', 'icon.png'))
    if not os.path.exists(rvt_icon_path):
        rvt_icon_path = os.path.abspath(os.path.join(root, '..', 'mahach666', 'RvtIO', 'src', 'RvtIO', 'icon.png'))
    if not os.path.exists(rvt_icon_path):
        rvt_icon_path = os.path.join(root, 'src', 'IfcIO', 'icon.png')
    im_rvt = Image.open(rvt_icon_path)
    arr = np.array(im_rvt, dtype=np.float64)

    c_bg = np.array([18.0, 22.0, 28.0])
    c_0  = np.array([122.0, 74.0, 18.0])  # shadow
    c_1  = np.array([201.0, 122.0, 43.0]) # mid
    c_2  = np.array([242.0, 163.0, 58.0]) # bright
    c_3  = np.array([255.0, 216.0, 74.0]) # highlight
    bases_rvt = [c_bg, c_0, c_1, c_2, c_3]

    bases_target = [
        np.array([18.0, 22.0, 28.0]),   # bg
        np.array([46.0, 16.0, 101.0]),  # shadow #2E1065
        np.array([126.0, 34.0, 206.0]), # mid #7E22CE
        np.array([168.0, 85.0, 247.0]), # bright #A855F7
        np.array([216.0, 180.0, 254.0]) # highlight #D8B4FE
    ]

    def decompose(rgb):
        for i, b in enumerate(bases_rvt):
            if np.allclose(rgb, b, atol=1.0):
                return [(i, 1.0)]
        pairs = [(0, 1), (1, 2), (2, 3), (3, 4), (0, 2), (0, 3), (0, 4), (1, 3), (1, 4), (2, 4)]
        best_pair = None
        best_dist = 1e9
        best_t = 0.0
        for i, j in pairs:
            v = bases_rvt[j] - bases_rvt[i]
            v_norm2 = np.dot(v, v)
            if v_norm2 < 1e-6: continue
            t = np.clip(np.dot(rgb - bases_rvt[i], v) / v_norm2, 0.0, 1.0)
            proj = bases_rvt[i] + t * v
            dist = np.linalg.norm(rgb - proj)
            if dist < best_dist:
                best_dist = dist
                best_pair = (i, j)
                best_t = t
        return [(best_pair[0], 1.0 - best_t), (best_pair[1], best_t)]

    unique_rgbs = {}
    for y in range(128):
        for x in range(128):
            rgb = tuple(arr[y, x, :3].astype(int))
            if rgb not in unique_rgbs:
                unique_rgbs[rgb] = decompose(np.array(rgb, dtype=np.float64))

    out = np.zeros_like(arr, dtype=np.uint8)
    for y in range(128):
        for x in range(128):
            alpha = arr[y, x, 3]
            if alpha == 0:
                out[y, x] = [0, 0, 0, 0]
            elif alpha < 255:
                out[y, x, :3] = bases_target[0]
                out[y, x, 3] = int(alpha)
            else:
                rgb = tuple(arr[y, x, :3].astype(int))
                decomp = unique_rgbs[rgb]
                rec = sum(w * bases_target[idx] for idx, w in decomp)
                rec = np.clip(np.round(rec), 0, 255).astype(np.uint8)
                out[y, x, :3] = rec
                out[y, x, 3] = 255
    return Image.fromarray(out, 'RGBA')

def main():
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    docs_images = os.path.join(root, 'docs', 'images')
    os.makedirs(docs_images, exist_ok=True)
    src_dir = os.path.join(root, 'src', 'IfcIO')
    os.makedirs(src_dir, exist_ok=True)

    letters = [I_cap, f_lower, c_lower, I_cap, O_cap]

    # 1. ifcio-logo.svg (590x214, rx=18, block=14, base_xs=[23, 135, 247, 359, 471], start_y=38, shadow=2)
    logo_svg = build_svg(590, 214, 18, 14, [23, 135, 247, 359, 471], 38, 2, letters, "IfcIO")
    with open(os.path.join(docs_images, 'ifcio-logo.svg'), 'w', encoding='utf-8') as f:
        f.write(logo_svg)
    print("Wrote docs/images/ifcio-logo.svg")

    # 2. ifcio-logo.png (1180x428 2x scale RGBA)
    logo_png = render_png(590, 214, 18, 14, [23, 135, 247, 359, 471], 38, 2, letters, scale=2, alpha_bg=True)
    logo_png.save(os.path.join(docs_images, 'ifcio-logo.png'))
    print("Wrote docs/images/ifcio-logo.png")

    # 3. ifcio-banner.svg (1280x320, rx=0, block=24, base_xs=[178, 370, 562, 754, 946], start_y=46, shadow=3.4)
    banner_svg = build_svg(1280, 320, 0, 24, [178, 370, 562, 754, 946], 46, 3.4, letters, "IfcIO")
    with open(os.path.join(docs_images, 'ifcio-banner.svg'), 'w', encoding='utf-8') as f:
        f.write(banner_svg)
    print("Wrote docs/images/ifcio-banner.svg")

    # 4. ifcio-banner.png (1280x320 RGB)
    banner_png = render_png(1280, 320, 0, 24, [178, 370, 562, 754, 946], 46, 3.4, letters, scale=1, alpha_bg=False)
    banner_png.save(os.path.join(docs_images, 'ifcio-banner.png'))
    print("Wrote docs/images/ifcio-banner.png")

    # 5. icon.png (128x128 RGBA)
    icon_png = render_canonical_icon()
    icon_png.save(os.path.join(src_dir, 'icon.png'))
    print("Wrote src/IfcIO/icon.png")

if __name__ == '__main__':
    main()
