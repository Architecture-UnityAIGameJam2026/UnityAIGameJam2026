from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1]
out = root / '.gameharness' / 'artifacts'
sheet = Image.new('RGB', (1980, 1830), '#141322')
draw = ImageDraw.Draw(sheet)
font = ImageFont.truetype(str(root / 'Assets/garam/UI/Fonts/Rajdhani-SemiBold.ttf'), 30)
small = ImageFont.truetype(str(root / 'Assets/garam/UI/Fonts/IBMPlexMono-Regular.ttf'), 14)
draw.text((30, 20), 'ON YOUR MARK / SENSELINK', fill='#F3EEDD', font=font)
draw.text((30, 62), 'SOFT SIGNAL V7  /  LAVENDER + IVORY + LIME  /  1920 x 1080', fill='#B7A6F6', font=small)
for i, (name, caption) in enumerate([
    ('field', '01  FIELD / CLEAR SIGHTLINE'), ('channels', '02  LINK / CHOOSE A CHANNEL'),
    ('support', '03  SUPPORT / CHOOSE AN ACTION'), ('request', '04  EXCHANGE / MUTUAL CONSENT'),
    ('critical', '05  PARTNER IN CRISIS'), ('awakened', '06  AUTONOMY RESTORED')
]):
    x, y = 30 + (i % 2) * 975, 110 + (i // 2) * 565
    draw.text((x, y), caption, fill='#D5EF86' if i % 2 else '#B7A6F6', font=small)
    im = Image.open(out / f'unity-{name}.png').convert('RGB')
    if im.size != (1920, 1080):
        raise ValueError(f'Unexpected capture size: {name}: {im.size}')
    sheet.paste(im.resize((945, 532), Image.Resampling.LANCZOS), (x, y + 26))
sheet.save(out / 'OnYourMark-UI-overview.png')
print(out / 'OnYourMark-UI-overview.png')
