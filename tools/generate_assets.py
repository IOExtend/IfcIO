import os
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
    0: '#431407', # Deep shadow
    1: '#C2410C', # Mid tone
    2: '#EA580C', # Bright tone
    3: '#FDBA74', # Highlight
}

# IO: Electric Royal Blue / Azure
pal_io = {
    0: '#0B1B3D', # Deep shadow
    1: '#1D4ED8', # Mid tone
    2: '#3B82F6', # Bright tone
    3: '#93C5FD', # Highlight
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
                            shadow_rects.append(f'<rect x="{ox + start_c * block_size}" y="{oy + r * block_size}" width="{span_w}" height="{block_size}" fill="{pal[0]}"/>')
                        else:
                            c += 1
        out.append(f'<g id="{group_name}-shadows">' + ''.join(shadow_rects) + '</g>')

        # Front face layers (Mid, Bright, Highlight)
        for tone_level in (1, 2, 3):
            tone_rects = []
            char_match = str(tone_level)
            color_hex = pal[tone_level]
            for idx in letter_indices:
                grid = letters[idx]
                base_x = base_xs[idx]
                for r in range(9):
                    c = 0
                    while c < 6:
                        if grid[r][c] == char_match:
                            start_c = c
                            while c < 6 and grid[r][c] == char_match:
                                c += 1
                            span_w = (c - start_c) * block_size
                            tone_rects.append(f'<rect x="{base_x + start_c * block_size}" y="{start_y + r * block_size}" width="{span_w}" height="{block_size}" fill="{color_hex}"/>')
                        else:
                            c += 1
            if tone_rects:
                out.append(f'<g id="{group_name}-tone{tone_level}">' + ''.join(tone_rects) + '</g>')

    out.append('</g></svg>')
    return '\n'.join(out)

def render_png(width, height, rx, block_size, base_xs, start_y, shadow_offset, letters, transparent_bg=False):
    bg_color = (0, 0, 0, 0) if transparent_bg else (18, 22, 28, 255)
    img = Image.new('RGBA', (width, height), bg_color)
    draw = ImageDraw.Draw(img)

    def hex_to_rgba(h):
        h = h.lstrip('#')
        return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)

    for group_name, letter_indices, pal in [('ifc', [0, 1, 2], pal_ifc), ('io', [3, 4], pal_io)]:
        shadow_col = hex_to_rgba(pal[0])
        for idx in letter_indices:
            grid = letters[idx]
            base_x = base_xs[idx]
            for step in range(1, 7):
                ox = base_x + step * shadow_offset
                oy = start_y + step * shadow_offset
                for r in range(9):
                    for c in range(6):
                        if grid[r][c] != ' ':
                            x0 = ox + c * block_size
                            y0 = oy + r * block_size
                            draw.rectangle([x0, y0, x0 + block_size - 1, y0 + block_size - 1], fill=shadow_col)

        for tone_level in (1, 2, 3):
            tone_col = hex_to_rgba(pal[tone_level])
            char_match = str(tone_level)
            for idx in letter_indices:
                grid = letters[idx]
                base_x = base_xs[idx]
                for r in range(9):
                    for c in range(6):
                        if grid[r][c] == char_match:
                            x0 = base_x + c * block_size
                            y0 = start_y + r * block_size
                            draw.rectangle([x0, y0, x0 + block_size - 1, y0 + block_size - 1], fill=tone_col)

    return img

def render_icon(size=128):
    letters = [I_cap, f_lower, c_lower, I_cap, O_cap]
    img = Image.new('RGBA', (size, size), (18, 22, 28, 255))
    draw = ImageDraw.Draw(img)

    block_size = 2
    shadow_offset = 1

    total_w = (6 * 5 + 4 * 1 + 2) * block_size
    start_x = (size - total_w) // 2
    start_y = (size - 9 * block_size) // 2

    base_xs = [
        start_x,
        start_x + 7 * block_size,
        start_x + 14 * block_size,
        start_x + 23 * block_size,
        start_x + 30 * block_size
    ]

    def hex_to_rgba(h):
        h = h.lstrip('#')
        return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), 255)

    for group_name, letter_indices, pal in [('ifc', [0, 1, 2], pal_ifc), ('io', [3, 4], pal_io)]:
        shadow_col = hex_to_rgba(pal[0])
        for idx in letter_indices:
            grid = letters[idx]
            base_x = base_xs[idx]
            for step in range(1, 4):
                ox = base_x + step * shadow_offset
                oy = start_y + step * shadow_offset
                for r in range(9):
                    for c in range(6):
                        if grid[r][c] != ' ':
                            x0 = ox + c * block_size
                            y0 = oy + r * block_size
                            draw.rectangle([x0, y0, x0 + block_size - 1, y0 + block_size - 1], fill=shadow_col)

        for tone_level in (1, 2, 3):
            tone_col = hex_to_rgba(pal[tone_level])
            char_match = str(tone_level)
            for idx in letter_indices:
                grid = letters[idx]
                base_x = base_xs[idx]
                for r in range(9):
                    for c in range(6):
                        if grid[r][c] == char_match:
                            x0 = base_x + c * block_size
                            y0 = start_y + r * block_size
                            draw.rectangle([x0, y0, x0 + block_size - 1, y0 + block_size - 1], fill=tone_col)

    return img

def main():
    os.makedirs('docs/images', exist_ok=True)
    os.makedirs('src/IfcIO', exist_ok=True)

    letters = [I_cap, f_lower, c_lower, I_cap, O_cap]

    # 1. Banner: 1280 x 320
    b_block = 20
    b_shadow = 3
    b_total_w = (6 * 5 + 4 * 1 + 2) * b_block
    b_start_x = (1280 - b_total_w) // 2
    b_start_y = (320 - 9 * b_block) // 2
    b_xs = [
        b_start_x,
        b_start_x + 7 * b_block,
        b_start_x + 14 * b_block,
        b_start_x + 23 * b_block,
        b_start_x + 30 * b_block
    ]

    svg_banner = build_svg(1280, 320, 16, b_block, b_xs, b_start_y, b_shadow, letters, "IfcIO Banner")
    with open('docs/images/ifcio-banner.svg', 'w', encoding='utf-8') as f:
        f.write(svg_banner)

    png_banner = render_png(1280, 320, 16, b_block, b_xs, b_start_y, b_shadow, letters)
    png_banner.save('docs/images/ifcio-banner.png')
    print("Created docs/images/ifcio-banner.svg and .png")

    # 2. Logo: 590 x 214
    l_block = 12
    l_shadow = 2
    l_total_w = (6 * 5 + 4 * 1 + 2) * l_block
    l_start_x = (590 - l_total_w) // 2
    l_start_y = (214 - 9 * l_block) // 2
    l_xs = [
        l_start_x,
        l_start_x + 7 * l_block,
        l_start_x + 14 * l_block,
        l_start_x + 23 * l_block,
        l_start_x + 30 * l_block
    ]

    svg_logo = build_svg(590, 214, 12, l_block, l_xs, l_start_y, l_shadow, letters, "IfcIO Logo")
    with open('docs/images/ifcio-logo.svg', 'w', encoding='utf-8') as f:
        f.write(svg_logo)

    png_logo_2x = render_png(590 * 2, 214 * 2, 24, l_block * 2, [x * 2 for x in l_xs], l_start_y * 2, l_shadow * 2, letters)
    png_logo_2x.save('docs/images/ifcio-logo.png')
    print("Created docs/images/ifcio-logo.svg and .png")

    # 3. Icons: 128 x 128
    icon = render_icon(128)
    icon.save('src/IfcIO/icon.png')
    print("Created src/IfcIO/icon.png")

if __name__ == '__main__':
    main()
