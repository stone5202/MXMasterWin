#!/usr/bin/env python3
# 產生各系列滑鼠的俯視示意圖（SVG）與 manifest.json，Windows 與 Mac 共用。
# 圖形全部由這支程式繪製，只參考官方手冊的比例與按鍵位置，不含官方線稿、照片或商標。
# 用法：python -I generate.py <輸出資料夾>
import json
import os
import sys

OUT = sys.argv[1]
W, H = 400, 520
ACCENT = '#1E6BF0'


def spline(points):
    """Closed Catmull-Rom curve; repeating a point makes a sharp corner."""
    n = len(points)
    parts = ['M%.1f %.1f' % points[0]]
    for i in range(n):
        p0, p1, p2, p3 = points[i - 1], points[i], points[(i + 1) % n], points[(i + 2) % n]
        c1 = (p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6)
        parts.append('C%.1f %.1f %.1f %.1f %.1f %.1f' % (c1 + c2 + p2))
    return ' '.join(parts) + ' Z'


def poly(points):
    return 'M' + ' L'.join('%.1f %.1f' % p for p in points) + ' Z'


def smooth(points):
    """Open curve through the points, for seams."""
    pts = [points[0]] + list(points) + [points[-1]]
    parts = ['M%.1f %.1f' % points[0]]
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = pts[i - 1], pts[i], pts[i + 1], pts[i + 2]
        c1 = (p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6)
        c2 = (p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6)
        parts.append('C%.1f %.1f %.1f %.1f %.1f %.1f' % (c1 + c2 + p2))
    return ' '.join(parts)


def seam_y(seam, x):
    if x <= seam[0][0]:
        return seam[0][1]
    for a, b in zip(seam, seam[1:]):
        if a[0] <= x <= b[0]:
            return a[1] + (b[1] - a[1]) * (x - a[0]) / (b[0] - a[0])
    return seam[-1][1]


class R:
    def __init__(self, x, y, w, h, r=4):
        self.x, self.y, self.w, self.h, self.r = x, y, w, h, r

    @property
    def center(self):
        return (self.x + self.w / 2, self.y + self.h / 2)

    def svg(self, attrs):
        return '<rect x="%g" y="%g" width="%g" height="%g" rx="%g" %s/>' % (self.x, self.y, self.w, self.h, self.r, attrs)


class P:
    def __init__(self, *points):
        self.points = points

    @property
    def center(self):
        return (sum(p[0] for p in self.points) / len(self.points), sum(p[1] for p in self.points) / len(self.points))

    def svg(self, attrs):
        return '<path d="%s" stroke-linejoin="round" %s/>' % (poly(self.points), attrs)


def key(kid, zh, en, label, shape=None, at=None, side=None, programmable=True):
    return {'id': kid, 'zh': zh, 'en': en, 'label': label, 'shape': shape, 'at': at, 'side': side, 'programmable': programmable}


def main_keys(left_at, right_at, wheel_at):
    return [
        key('left', '左鍵', 'Left click', 'L', at=left_at),
        key('right', '右鍵', 'Right click', 'R', at=right_at),
        key('wheel', '滾輪按下', 'Wheel click', 'M', at=wheel_at, side='right'),
    ]


def tilt_keys(wheel):
    y = wheel.y + wheel.h / 2
    return [
        key('tilt-left', '滾輪左撥', 'Wheel tilt left', '◀', shape=P((wheel.x - 8, y - 7), (wheel.x - 8, y + 7), (wheel.x - 14, y)), side='left'),
        key('tilt-right', '滾輪右撥', 'Wheel tilt right', '▶', shape=P((wheel.x + wheel.w + 8, y - 7), (wheel.x + wheel.w + 8, y + 7), (wheel.x + wheel.w + 14, y)), side='right'),
    ]


def side_pair(front, back, front_label, back_label, front_zh='下一頁', back_zh='上一頁', front_en='Forward', back_en='Back', prefix='', side='left'):
    return [
        key(prefix + 'front', front_zh, front_en, front_label, shape=front, side=side),
        key(prefix + 'back', back_zh, back_en, back_label, shape=back, side=side),
    ]


FAMILIES = []


def family(fid, title, wpids, outline, seam, wheel, keys, channel=None, models=None, palm=None, source='', raw=None, plates=None, channel_fill='#1F2127'):
    FAMILIES.append({'id': fid, 'title': title, 'wpids': wpids, 'outline': outline, 'seam': seam, 'wheel': wheel,
                     'keys': keys, 'channel': channel, 'models': models or [title], 'palm': palm, 'source': source,
                     'raw': raw, 'plates': plates, 'channel_fill': channel_fill})


# ---- G502 LIGHTSPEED ----
w = R(206, 131, 22, 76, 9)
family('g502', 'G502 LIGHTSPEED', ['407F'],
       [(194, 64), (316, 192), (327, 372), (202, 501), (68, 348), (101, 180)],
       [(106, 252), (146, 304), (202, 278), (238, 284), (300, 283), (318, 244)], w,
       main_keys((160, 116), (272, 128), (217, 184)) + tilt_keys(w) + [
           key('g8', 'DPI 上', 'DPI up', 'G8', shape=P((131, 104), (131, 174), (106, 178)), side='left'),
           key('g7', 'DPI 下', 'DPI down', 'G7', shape=P((105, 182), (132, 178), (135, 192), (108, 198)), side='left'),
           key('g6', '狙擊', 'Sniper', 'G6', shape=P((99, 202), (108, 201), (110, 220), (102, 224)), side='left'),
           key('g5', '下一頁', 'Forward', 'G5', shape=P((103, 228), (114, 227), (127, 296), (113, 298)), side='left'),
           key('g4', '上一頁', 'Back', 'G4', shape=P((114, 303), (128, 301), (134, 334), (122, 339)), side='left'),
           key('mode', '滾輪模式', 'Wheel mode', '', shape=R(207, 218, 26, 22, 5), programmable=False),
           key('g9', 'G9', 'G9', 'G9', shape=R(208, 249, 26, 29, 5), side='right'),
       ], channel=[(193.6, 99), (236, 99), (238.8, 284), (203, 279)], palm=[(122, 360), (144, 392), (184, 400), (212, 418), (216, 448), (210, 476), (204, 500)],
       raw='M194 64 L192 98 L236 98 L230 52 C256 68 280 80 292 92 C304 108 310 148 316 192 C318 228 316 260 318 284 C320 314 326 344 327 372 '
           'C328 404 304 452 274 478 C260 490 244 496 232 498.8 L217 494 L202.6 501.4 C184 498 162 486 148 474 L76.6 367 C70 360 68 356 68 348 '
           'C66 320 68 296 72.4 276 C76 264 88 256 97.6 246.8 L101 226 L95.6 212 L101.8 180 L131.2 100 Z',
       plates=[[(194, 66), (193.2, 99), (202.6, 278), (144, 304), (131.6, 176), (132.6, 105)],
               [(231, 56), (236.6, 99), (239.2, 284), (299.2, 283), (311, 194), (302, 128), (289, 96)]],
       source='G502 LIGHTSPEED setup guide')

# ---- G502 X / X PLUS ----
w = R(195, 122, 22, 72, 9)
family('g502x', 'G502 X 系列', ['4099', '409F'],
       [(184, 50), (184, 50), (186, 92), (186, 92), (224, 92), (224, 92), (226, 44), (226, 44), (262, 60), (290, 88), (306, 140), (316, 200),
        (318, 260), (330, 320), (322, 380), (294, 440), (250, 486), (205, 498), (160, 480), (122, 430), (92, 372), (72, 320), (70, 262), (84, 226),
        (100, 190), (108, 140), (122, 88), (150, 60)],
       [(96, 236), (150, 268), (188, 280), (226, 280), (272, 258), (324, 224)], w,
       main_keys((152, 130), (266, 130), (206, 170)) + tilt_keys(w) + [
           key('g8', 'DPI 上', 'DPI up', 'G8', shape=P((124, 96), (142, 88), (142, 142), (112, 152)), side='left'),
           key('g7', 'DPI 下', 'DPI down', 'G7', shape=P((110, 158), (142, 148), (145, 174), (106, 186)), side='left'),
           key('g6', 'DPI 切換', 'DPI shift', 'G6', shape=P((88, 214), (104, 192), (118, 200), (104, 240)), side='left'),
           key('g5', '下一頁', 'Forward', 'G5', shape=R(90, 248, 14, 42, 5), side='left'),
           key('g4', '上一頁', 'Back', 'G4', shape=R(94, 296, 14, 42, 5), side='left'),
           key('mode', '滾輪模式', 'Wheel mode', '', shape=R(195, 204, 22, 18, 5), programmable=False),
           key('g9', '切換設定檔', 'Profile cycle', 'G9', shape=R(195, 230, 22, 28, 5), side='right'),
       ], channel=[(186, 93), (224, 93), (226, 278), (188, 278)], palm=[(100, 340), (140, 380), (186, 392), (214, 412), (216, 450), (208, 498)],
       models=['G502 X PLUS', 'G502 X Wireless'], source='G502 X / X PLUS setup guide')

# ---- PRO ----
PRO = [(200, 42), (150, 48), (118, 80), (106, 140), (100, 210), (92, 280), (92, 340), (108, 410), (146, 466), (200, 484), (254, 466),
       (292, 410), (308, 340), (308, 280), (300, 210), (294, 140), (282, 80), (250, 48)]
PRO_SEAM = [(92, 262), (150, 286), (200, 292), (250, 286), (308, 262)]
w = R(189, 92, 22, 70, 9)
family('pro-wireless', 'PRO Wireless', ['4079'], PRO, PRO_SEAM, w,
       main_keys((146, 170), (254, 170), (200, 140)) + side_pair(R(95, 226, 12, 36, 5), R(92, 268, 12, 36, 5), 'G5', 'G4') +
       side_pair(R(293, 226, 12, 36, 5), R(296, 268, 12, 36, 5), 'G7', 'G6', prefix='r-', side='right'),
       source='PRO Wireless setup guide')
family('pro-superlight', 'PRO X SUPERLIGHT', ['4093'], PRO, PRO_SEAM, w,
       main_keys((146, 170), (254, 170), (200, 140)) + side_pair(R(95, 226, 12, 36, 5), R(92, 268, 12, 36, 5), 'Fwd', 'Back'),
       source='PRO X SUPERLIGHT setup guide')

# ---- G703 / G403 ----
w = R(190, 92, 22, 74, 9)
family('g703', 'G703 系列', ['405D', '4070', '4086'],
       [(196, 42), (150, 52), (116, 90), (102, 150), (92, 220), (84, 290), (92, 360), (116, 420), (156, 470), (206, 486), (256, 468), (298, 420),
        (318, 350), (322, 280), (312, 210), (300, 150), (282, 90), (244, 52)],
       [(88, 256), (150, 284), (202, 292), (258, 282), (321, 256)], w,
       main_keys((146, 170), (262, 170), (201, 146)) + [
           key('dpi', 'DPI 切換', 'DPI cycle', 'G6', shape=R(190, 178, 22, 24, 5), side='right')] +
       side_pair(R(90, 216, 13, 38, 5), R(86, 260, 13, 38, 5), 'G5', 'G4'),
       models=['G403 Wireless', 'G703', 'G703 Hero'], source='G703 LIGHTSPEED setup guide')

# ---- G903 / G900 ----
w = R(189, 96, 22, 74, 9)
family('g903', 'G903 系列', ['4053', '4067', '4087'],
       [(200, 40), (150, 46), (120, 76), (104, 130), (96, 190), (78, 240), (80, 290), (104, 330), (100, 380), (122, 436), (160, 474), (200, 486),
        (240, 474), (278, 436), (300, 380), (296, 330), (320, 290), (322, 240), (304, 190), (296, 130), (280, 76), (250, 46)],
       [(88, 298), (150, 314), (200, 320), (250, 314), (312, 298)], w,
       main_keys((140, 150), (260, 150), (200, 150)) + tilt_keys(w) + [
           key('mode', '滾輪模式', 'Wheel mode', '', shape=R(190, 180, 20, 18, 5), programmable=False),
           key('g10', 'DPI 下', 'DPI down', 'G10', shape=P((178, 208), (196, 208), (193, 250), (172, 250)), side='left'),
           key('g11', 'DPI 上', 'DPI up', 'G11', shape=P((204, 208), (222, 208), (228, 250), (207, 250)), side='right'),
       ] + side_pair(R(86, 240, 12, 34, 5), R(88, 280, 12, 34, 5), 'G5', 'G4') +
       side_pair(R(302, 240, 12, 34, 5), R(300, 280, 12, 34, 5), 'G7', 'G6', prefix='r-', side='right'),
       channel=[(184, 41), (216, 41), (236, 318), (164, 318)], channel_fill='#30333C', models=['G900', 'G903', 'G903 Hero'], source='G903 LIGHTSPEED setup guide')

# ---- G604 ----
w = R(190, 92, 22, 74, 9)
thumb = [R(66 + c * 17, 224 + r * 27, 14, 23, 4) for r in range(3) for c in range(2)]
family('g604', 'G604', ['4085'],
       [(200, 40), (158, 46), (130, 70), (116, 120), (104, 176), (68, 210), (58, 260), (64, 320), (92, 372), (120, 430), (162, 474), (210, 488),
        (258, 476), (298, 430), (322, 360), (326, 280), (318, 210), (304, 140), (284, 84), (250, 52)],
       [(104, 252), (150, 274), (200, 284), (260, 272), (324, 242)], w,
       main_keys((146, 170), (262, 170), (201, 146)) + tilt_keys(w) + [
           key('dpi-up', 'DPI 上', 'DPI up', 'DPI+', shape=R(160, 106, 14, 22, 4), side='left'),
           key('dpi-down', 'DPI 下', 'DPI down', 'DPI-', shape=R(160, 134, 14, 22, 4), side='left'),
           key('mode', '滾輪模式', 'Wheel mode', '', shape=R(191, 176, 20, 18, 5), programmable=False),
           key('link', '連線切換', 'Connection toggle', '', shape=R(191, 202, 20, 26, 5), programmable=False),
           key('thumb', '拇指鍵 G4–G9', 'Thumb buttons G4–G9', 'G4–G9', at=(89, 263), side='left'),
       ], models=['G604'], source='G604 setup guide')
FAMILIES[-1]['extra'] = thumb

# ---- G603 ----
w = R(190, 96, 22, 72, 9)
family('g603', 'G603', ['406C'],
       [(198, 46), (156, 52), (122, 84), (106, 140), (96, 210), (86, 280), (92, 350), (114, 414), (154, 464), (204, 482), (254, 464), (296, 414),
        (316, 346), (320, 270), (312, 200), (300, 140), (280, 84), (242, 52)],
       [(90, 258), (150, 284), (202, 292), (258, 282), (319, 256)], w,
       main_keys((148, 172), (260, 172), (201, 146)) + [key('dpi', 'DPI 切換', 'DPI cycle', 'DPI', shape=R(191, 180, 20, 22, 5), side='right')] +
       side_pair(R(93, 218, 13, 36, 5), R(89, 260, 13, 36, 5), 'Fwd', 'Back'),
       models=['G603'], source='G603 setup guide (illustration only)')

# ---- G305 ----
w = R(189, 92, 22, 70, 9)
family('g305', 'G305', ['4074'],
       [(200, 44), (152, 50), (122, 80), (110, 140), (104, 210), (100, 280), (104, 350), (122, 410), (158, 458), (200, 474), (242, 458), (278, 410),
        (296, 350), (300, 280), (296, 210), (290, 140), (278, 80), (248, 50)],
       [(100, 256), (150, 280), (200, 288), (250, 280), (300, 256)], w,
       main_keys((148, 170), (252, 170), (200, 140)) + [key('dpi', 'DPI 切換', 'DPI cycle', 'G6', shape=R(190, 174, 20, 24, 6), side='right')] +
       side_pair(R(103, 216, 12, 36, 5), R(101, 258, 12, 36, 5), 'G4', 'G5'),
       models=['G305'], source='G305 setup guide')

# ---- G705 ----
w = R(190, 98, 20, 62, 9)
family('g705', 'G705', ['409D'],
       [(200, 58), (156, 62), (124, 90), (108, 146), (100, 212), (96, 280), (104, 346), (130, 400), (166, 436), (200, 446), (234, 436), (270, 400),
        (296, 346), (304, 280), (300, 212), (292, 146), (276, 90), (244, 62)],
       [(98, 232), (150, 258), (200, 266), (250, 258), (302, 232)], w,
       main_keys((150, 164), (250, 164), (200, 134)) + [key('dpi', 'DPI 切換', 'DPI cycle', 'DPI', shape=R(190, 172, 20, 20, 6), side='right')] +
       side_pair(R(101, 204, 12, 32, 5), R(99, 242, 12, 32, 5), 'Fwd', 'Back'),
       models=['G705'], source='G705 setup guide')

class C:
    """A round part with its own fill, such as a trackball."""

    def __init__(self, x, y, r, fill):
        self.x, self.y, self.r, self.fill = x, y, r, fill

    @property
    def center(self):
        return (self.x, self.y)

    def svg(self, attrs):
        return '<circle cx="%g" cy="%g" r="%g" fill="%s" stroke="#1B1D23" stroke-width="1.6"/>' % (self.x, self.y, self.r, self.fill)


def mode_key(shape):
    return key('mode', '滾輪模式', 'Wheel mode', 'Mode', shape=shape, side='right')


def gesture_key(shape):
    return key('gesture', '手勢鍵', 'Gesture button', 'GS', shape=shape, side='left')


def thumb_wheel(shape):
    return key('thumb-wheel', '拇指滾輪', 'Thumb wheel', 'TW', shape=shape, side='left')


# ---- G602 ----
w = R(198, 96, 20, 70, 9)
family('g602', 'G602', ['402C'],
       [(206, 46), (166, 54), (140, 84), (128, 140), (122, 196), (104, 232), (82, 268), (84, 336), (110, 400), (150, 452), (200, 480), (250, 470),
        (292, 424), (318, 360), (324, 284), (312, 204), (296, 140), (272, 88), (240, 54)],
       [(122, 240), (168, 258), (210, 262), (258, 250), (323, 216)], w,
       main_keys((166, 200), (266, 170), (208, 140)) + [
           key('perf', '效能／續航切換', 'Performance switch', '', shape=R(199, 176, 18, 16, 4), programmable=False),
           key('g10', 'DPI 上', 'DPI up', 'G10', shape=R(122, 176, 12, 22, 4), side='left'),
           key('g11', 'DPI 下', 'DPI down', 'G11', shape=R(117, 202, 12, 22, 4), side='left'),
           key('thumb', '拇指鍵 G4–G9', 'Thumb buttons G4–G9', 'G4–G9', at=(106, 284), side='left'),
       ], models=['G602'], source='G602 setup guide')
FAMILIES[-1]['extra'] = [R(88 + c * 17, 246 + r * 27, 14, 23, 4) for r in range(3) for c in range(2)]

# ---- MX Master / 2S ----
w = R(201, 100, 22, 72, 9)
family('mx-master', 'MX Master 系列', ['4041', '4060', '4071', '4069'],
       [(212, 46), (172, 52), (148, 82), (140, 140), (136, 210), (132, 254), (96, 270), (72, 312), (74, 372), (104, 430), (150, 476), (200, 490),
        (256, 476), (296, 430), (320, 360), (322, 280), (314, 200), (300, 130), (276, 78), (244, 50)],
       [(134, 240), (170, 252), (212, 258), (262, 246), (320, 214)], w,
       main_keys((172, 170), (268, 160), (212, 140)) + [
           mode_key(R(203, 184, 18, 18, 4)),
           thumb_wheel(R(140, 168, 14, 46, 6)),
           gesture_key(P((86, 302), (126, 282), (132, 340), (96, 372))),
       ] + side_pair(R(137, 222, 14, 20, 4), R(134, 246, 14, 20, 4), 'Fwd', 'Back'),
       models=['MX Master', 'MX Master 2S'], source='MX Master 2S getting started')

# ---- MX Master 3 ----
w = R(198, 88, 24, 82, 9)
family('mx-master-3', 'MX Master 3', ['4082'],
       [(206, 40), (166, 50), (148, 84), (146, 150), (140, 214), (108, 232), (78, 270), (70, 330), (90, 400), (130, 460), (182, 488), (232, 480),
        (280, 440), (316, 370), (326, 290), (316, 210), (298, 140), (272, 84), (240, 50)],
       [(142, 232), (190, 250), (230, 246), (270, 230), (322, 200)], w,
       main_keys((176, 150), (268, 150), (210, 130)) + [
           mode_key(R(200, 182, 20, 20, 4)),
           thumb_wheel(R(148, 180, 18, 40, 7)),
           gesture_key(P((88, 282), (130, 252), (136, 320), (98, 356))),
       ] + side_pair(R(146, 226, 16, 18, 4), R(143, 248, 16, 18, 4), 'Fwd', 'Back'),
       models=['MX Master 3'], source='MX Master 3 getting started')

# ---- MX Anywhere 2 / 2S ----
w = R(190, 118, 20, 54, 8)
family('mx-anywhere-2', 'MX Anywhere 2 系列', ['404A', '4063', '4072', '406A'],
       [(200, 84), (160, 88), (132, 112), (118, 160), (112, 230), (112, 300), (122, 360), (150, 410), (200, 428), (250, 410), (278, 360), (288, 300),
        (288, 230), (282, 160), (268, 112), (240, 88)],
       [(112, 246), (150, 262), (200, 268), (250, 262), (288, 246)], w,
       main_keys((152, 190), (248, 190), (200, 150)) + [mode_key(R(190, 182, 20, 18, 4))] +
       side_pair(R(114, 212, 10, 26, 4), R(113, 244, 10, 26, 4), 'Fwd', 'Back'),
       models=['MX Anywhere 2', 'MX Anywhere 2S'], source='MX Anywhere 2S getting started')

# ---- MX Anywhere 3 ----
w = R(189, 112, 22, 56, 8)
family('mx-anywhere-3', 'MX Anywhere 3', ['4090'],
       [(200, 76), (150, 80), (122, 104), (108, 156), (104, 230), (106, 310), (120, 372), (154, 420), (200, 436), (246, 420), (280, 372), (294, 310),
        (296, 230), (292, 156), (278, 104), (250, 80)],
       [(105, 252), (150, 266), (200, 270), (250, 266), (295, 252)], w,
       main_keys((148, 190), (252, 190), (200, 146)) + [mode_key(R(191, 178, 18, 22, 5))] +
       side_pair(R(106, 212, 10, 28, 4), R(106, 246, 10, 28, 4), 'Fwd', 'Back'),
       models=['MX Anywhere 3'], source='MX Anywhere 3 getting started')

# ---- MX Ergo ----
w = R(222, 92, 20, 62, 8)
family('mx-ergo', 'MX Ergo', ['406F'],
       [(232, 46), (194, 50), (172, 76), (166, 130), (158, 180), (120, 196), (92, 236), (86, 296), (104, 346), (146, 374), (162, 410), (190, 456),
        (230, 484), (270, 472), (300, 424), (320, 360), (326, 290), (316, 210), (300, 130), (276, 76)],
       [(164, 196), (200, 232), (240, 238), (280, 222), (322, 190)], w,
       main_keys((194, 150), (284, 150), (232, 132)) + tilt_keys(w) + [
           key('easy-switch', 'Easy-Switch', 'Easy-Switch', '', shape=R(223, 164, 18, 34, 7), programmable=False),
           key('dpi', '精準模式', 'Precision mode', 'DPI', shape=R(176, 208, 20, 20, 10), side='left'),
           key('ball', '軌跡球', 'Trackball', '', shape=C(134, 288, 54, '#8B93A3'), programmable=False),
       ] + side_pair(R(160, 112, 11, 30, 4), R(157, 148, 11, 30, 4), 'Fwd', 'Back'),
       models=['MX Ergo'], source='MX Ergo getting started')

# ---- MX Vertical ----
w = R(205, 112, 20, 60, 8)
family('mx-vertical', 'MX Vertical', ['407B'],
       [(214, 62), (178, 68), (156, 98), (146, 160), (140, 240), (138, 320), (150, 392), (178, 444), (216, 462), (252, 444), (276, 396), (284, 330),
        (284, 250), (274, 170), (256, 104), (238, 72)],
       [(140, 248), (180, 262), (215, 266), (250, 258), (284, 238)], w,
       main_keys((176, 200), (252, 196), (215, 150)) + [
           key('dpi', '游標速度', 'Cursor speed', 'DPI', shape=R(206, 84, 18, 18, 5), side='right'),
       ] + side_pair(R(142, 204, 10, 28, 4), R(141, 238, 10, 28, 4), 'Fwd', 'Back'),
       models=['MX Vertical'], source='MX Vertical getting started (photo)')

# ---- M720 ----
w = R(197, 104, 20, 64, 8)
family('m720', 'M720 Triathlon', ['405E'],
       [(206, 52), (168, 58), (142, 90), (132, 150), (126, 220), (98, 262), (88, 320), (100, 390), (136, 444), (190, 476), (240, 470), (284, 430),
        (312, 366), (318, 290), (308, 210), (292, 140), (268, 88), (238, 58)],
       [(128, 250), (170, 262), (210, 266), (256, 254), (317, 220)], w,
       main_keys((166, 170), (262, 160), (207, 140)) + [
           key('easy-switch', 'Easy-Switch', 'Easy-Switch', '', shape=R(198, 180, 18, 20, 5), programmable=False),
           key('gesture', '拇指鍵', 'Thumb button', 'Thumb', shape=P((98, 304), (128, 286), (132, 338), (104, 360)), side='left'),
       ] + side_pair(R(128, 210, 12, 26, 4), R(125, 240, 12, 26, 4), 'Fwd', 'Back'),
       models=['M720'], source='M720 setup guide')

# ---- M585 / M590 ----
w = R(191, 100, 18, 54, 8)
family('m585', 'M585／M590', ['406B'],
       [(200, 64), (160, 68), (132, 98), (118, 160), (112, 240), (112, 320), (124, 384), (158, 430), (200, 446), (242, 430), (276, 384), (288, 320),
        (288, 240), (282, 160), (268, 98), (240, 68)],
       [(112, 248), (150, 266), (200, 272), (250, 266), (288, 248)], w,
       main_keys((152, 180), (248, 180), (200, 130)) + tilt_keys(w) + [
           key('easy-switch', 'Easy-Switch', 'Easy-Switch', '', shape=R(192, 162, 16, 16, 4), programmable=False),
           key('channel', '頻道燈', 'Channel LED', '', shape=R(193, 186, 14, 30, 6), programmable=False),
       ] + side_pair(R(113, 208, 10, 28, 4), R(112, 242, 10, 28, 4), 'Fwd', 'Back'),
       models=['M585', 'M590'], source='M585 / M590 setup guide')

# ---- M705 ----
w = R(195, 104, 18, 60, 8)
family('m705', 'M705 Marathon', ['406D'],
       [(204, 54), (164, 58), (136, 88), (124, 150), (118, 220), (100, 264), (94, 322), (110, 392), (146, 442), (196, 466), (246, 456), (288, 410),
        (312, 346), (316, 272), (304, 196), (290, 130), (264, 82), (236, 58)],
       [(120, 246), (170, 264), (210, 268), (258, 256), (315, 222)], w,
       main_keys((162, 170), (258, 160), (204, 140)) + [mode_key(R(196, 176, 16, 22, 5))] +
       side_pair(R(120, 206, 11, 28, 4), R(116, 238, 11, 28, 4), 'Fwd', 'Back'),
       models=['M705'], source='M705 getting started (photo only)')

# ---- M545 ----
w = R(191, 100, 18, 56, 8)
family('m545', 'M545', ['4052'],
       [(200, 62), (160, 66), (132, 96), (120, 156), (116, 232), (114, 312), (126, 384), (158, 432), (200, 450), (242, 432), (274, 384), (286, 312),
        (284, 232), (280, 156), (268, 96), (240, 66)],
       [(115, 248), (152, 266), (200, 270), (248, 266), (285, 248)], w,
       main_keys((154, 180), (246, 180), (200, 130)) + tilt_keys(w) + [
           key('extra', '滾輪下方鍵', 'Button below wheel', 'M2', shape=R(192, 166, 16, 16, 5), side='right'),
       ] + side_pair(R(116, 210, 10, 26, 4), R(115, 242, 10, 26, 4), 'Fwd', 'Back'),
       models=['M545'], source='M545 setup guide')

# ---- M325 ----
w = R(191, 124, 18, 50, 8)
family('m325', 'M325', ['400A'],
       [(200, 96), (166, 100), (142, 124), (128, 172), (122, 240), (124, 310), (138, 368), (166, 408), (200, 420), (234, 408), (262, 368), (276, 310),
        (278, 240), (272, 172), (258, 124), (234, 100)],
       [(123, 240), (160, 254), (200, 258), (240, 254), (277, 240)], w,
       main_keys((158, 190), (242, 190), (200, 150)) + tilt_keys(w),
       models=['M325'], source='M325 getting started')

# ---- Wireless Touchpad ----
family('touchpad', 'Wireless Touchpad', ['4011'],
       [(70, 120), (330, 120), (330, 410), (70, 410)],
       [(70, 344), (200, 344), (330, 344)], None,
       [key('left', '左鍵', 'Left click', 'L', at=(136, 378)), key('right', '右鍵', 'Right click', 'R', at=(264, 378))],
       raw='M100 120 H300 Q330 120 330 150 V380 Q330 410 300 410 H100 Q70 410 70 380 V150 Q70 120 100 120 Z',
       models=['Wireless Touchpad'], source='Wireless Touchpad getting started')
FAMILIES[-1]['flat'] = True
FAMILIES[-1]['lines'] = [((70, 344), (330, 344)), ((200, 344), (200, 410))]

# ---- POWERPLAY (no buttons) ----
family('powerplay', 'G POWERPLAY', ['405F'],
       [(40, 150), (360, 150), (360, 450), (40, 450)],
       [(40, 300), (360, 300)], None, [],
       raw='M58 150 H342 Q360 150 360 168 V432 Q360 450 342 450 H58 Q40 450 40 432 V168 Q40 150 58 150 Z',
       models=['G POWERPLAY'], source='POWERPLAY setup guide')
FAMILIES[-1]['flat'] = True
FAMILIES[-1]['extra'] = [R(52, 104, 130, 38, 12)]

# ---- B330 / M330 / M331 (not in the libratbag list; identified on real hardware) ----
w = R(195, 118, 20, 66, 8)
family('m330', 'M330／M331', ['4057'],
       [(205, 72), (146, 97), (109, 122), (96, 203), (86, 260), (74, 335), (84, 403), (134, 453), (202, 468), (271, 447), (319, 384), (321, 316),
        (311, 228), (299, 128), (259, 97)],
       [(96, 300), (205, 300), (320, 300)], w,
       main_keys((150, 210), (260, 210), (205, 160)),
       models=['B330', 'M330', 'M331'], source='M275/M280/M330/M331 setup guide')
FAMILIES[-1]['flat'] = True
FAMILIES[-1]['panel'] = [(205, 76), (150, 100), (116, 130), (106, 200), (106, 270), (122, 330), (160, 372), (205, 384), (250, 372), (288, 330),
                         (304, 270), (304, 200), (292, 130), (258, 100)]
FAMILIES[-1]['lines'] = [((205, 76), (205, 112)), ((205, 190), (205, 290))]
FAMILIES[-1]['extra'] = [C(205, 297, 3.5, '#36C26E')]

PENDING = {}


def shape_center(k):
    return k['at'] if k['at'] else k['shape'].center


def draw(f, labeled):
    o = []
    o.append('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" viewBox="0 0 %d %d">' % (W, H, W, H))
    outline = f['raw'] or spline(f['outline'])
    o.append('<defs>'
             '<linearGradient id="body" x1="0.25" y1="0" x2="0.75" y2="1"><stop offset="0" stop-color="#555A66"/><stop offset="1" stop-color="#2A2D35"/></linearGradient>'
             '<linearGradient id="plate" x1="0.2" y1="0" x2="0.6" y2="1"><stop offset="0" stop-color="#6F7583"/><stop offset="1" stop-color="#454954"/></linearGradient>'
             '<linearGradient id="wheel" x1="0" y1="0" x2="1" y2="0"><stop offset="0" stop-color="#7B818E"/><stop offset=".5" stop-color="#C4C9D2"/><stop offset="1" stop-color="#7B818E"/></linearGradient>'
             '<filter id="shadow" x="-20%%" y="-20%%" width="140%%" height="140%%"><feDropShadow dx="2" dy="7" stdDeviation="8" flood-color="#141A26" flood-opacity=".28"/></filter>'
             '<clipPath id="shell"><path d="%s"/></clipPath></defs>' % outline)
    o.append('<path d="%s" fill="url(#body)" filter="url(#shadow)"/>' % outline)
    o.append('<g clip-path="url(#shell)">')
    seam = f['seam']
    ch = f['channel']
    middle = f['wheel'].x + f['wheel'].w / 2 if f['wheel'] else 200
    xl = ch[0][0] if ch else middle - 1
    xr = ch[1][0] if ch else middle + 1
    left =[(0, 0), (xl, 0), (ch[3][0] if ch else xl, seam_y(seam, xl))] + [p for p in reversed(seam) if p[0] < xl] + [(0, seam[0][1])]
    right = [(xr, 0), (W, 0), (W, seam[-1][1])] + [p for p in reversed(seam) if p[0] > xr] + [(ch[2][0] if ch else xr, seam_y(seam, xr))]
    if f['plates']:
        left, right = f['plates']
    if not f.get('flat'):
        o.append('<path d="%s" fill="url(#plate)" stroke="#1B1D23" stroke-width="1.6" stroke-linejoin="round"/>' % poly(left))
        o.append('<path d="%s" fill="url(#plate)" stroke="#1B1D23" stroke-width="1.6" stroke-linejoin="round"/>' % poly(right))
    if ch:
        o.append('<path d="%s" fill="%s"/>' % (poly(ch), f['channel_fill']))
    if f.get('panel'):
        o.append('<path d="%s" fill="url(#plate)" stroke="#1B1D23" stroke-width="1.6"/>' % spline(f['panel']))
    for a, b in f.get('lines', []):
        o.append('<line x1="%g" y1="%g" x2="%g" y2="%g" stroke="#1B1D23" stroke-width="1.8"/>' % (a + b))
    if f.get('palm'):
        pts = f['palm']
        o.append('<path d="%s" fill="none" stroke="#1B1D23" stroke-width="1.6"/>' % smooth(pts))
    if not f['plates'] and not f.get('flat') and f['wheel']:
        o.append('<path d="%s" fill="none" stroke="#FFFFFF" stroke-opacity=".07" stroke-width="16"/>' % smooth([(p[0], p[1] + 12) for p in seam]))
    o.append('</g>')
    wh = f['wheel']
    if wh:
        o.append('<rect x="%g" y="%g" width="%g" height="%g" rx="11" fill="#101116"/>' % (wh.x - 4, wh.y - 5, wh.w + 8, wh.h + 10))
        o.append('<rect x="%g" y="%g" width="%g" height="%g" rx="%g" fill="url(#wheel)" stroke="#8C929E" stroke-width=".8"/>' % (wh.x, wh.y, wh.w, wh.h, wh.r))
        y = wh.y + 6
        while y < wh.y + wh.h - 4:
            o.append('<line x1="%g" y1="%g" x2="%g" y2="%g" stroke="#2A2C33" stroke-opacity=".55" stroke-width="1.5"/>' % (wh.x + 3, y, wh.x + wh.w - 3, y))
            y += 6.5
    for shape in f.get('extra', []):
        o.append(shape.svg('fill="#7C828F" stroke="#1B1D23" stroke-width="1.2"'))
    for k in f['keys']:
        if k['shape'] is None:
            continue
        fill = '#34373F' if not k['programmable'] else '#878D9A'
        if k['id'].startswith('tilt'):
            fill = '#C1C6CF'
        o.append(k['shape'].svg('fill="%s" stroke="#1B1D23" stroke-width="1.2"' % fill))
    o.append('<path d="%s" fill="none" stroke="#7D8390" stroke-width="1.6"/>' % outline)
    if labeled:
        o.extend(labels(f))
    o.append('</svg>')
    return '\n'.join(o)


def labels(f):
    columns = {'left': [], 'right': []}
    center = sum(p[0] for p in f['outline']) / len(f['outline'])
    for k in f['keys']:
        if not k['label']:
            continue
        cx, cy = shape_center(k)
        side = k['side'] or ('left' if cx < center else 'right')
        columns[side].append((cy, k))
    out = []
    for side, items in columns.items():
        items.sort(key=lambda item: item[0])
        gap = 37
        placed = []
        for cy, _ in items:
            placed.append(max(cy, placed[-1] + gap) if placed else max(cy, 26))
        overflow = placed[-1] - (H - 26) if placed else 0
        if overflow > 0:
            placed = [p - overflow for p in placed]
            for i in range(len(placed) - 2, -1, -1):
                placed[i] = min(placed[i], placed[i + 1] - gap)
        for (cy, k), ly in zip(items, placed):
            cx, cy = shape_center(k)
            text = k['label']
            bw = max(30, 11 + len(text) * 9.2)
            bx = 6 if side == 'left' else W - 6 - bw
            ex = bx + bw if side == 'left' else bx
            out.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="%s" stroke-opacity=".75" stroke-width="1.3"/>' % (ex, ly, cx, cy, ACCENT))
            out.append('<circle cx="%.1f" cy="%.1f" r="3" fill="#FFFFFF" stroke="%s" stroke-width="1.4"/>' % (cx, cy, ACCENT))
            out.append('<rect x="%.1f" y="%.1f" width="%.1f" height="26" rx="13" fill="#FFFFFF" stroke="%s" stroke-width="1.6"/>' % (bx, ly - 13, bw, ACCENT))
            out.append('<text x="%.1f" y="%.1f" text-anchor="middle" font-family="Segoe UI, -apple-system, Helvetica, Arial, sans-serif" font-size="13.5" font-weight="700" fill="%s">%s</text>' % (bx + bw / 2, ly + 4.8, ACCENT, text))
    return out


os.makedirs(OUT, exist_ok=True)
manifest = {
    'note': 'Original illustrations. Proportions and button positions follow Logitech setup guides; no official artwork, photos or logos are included.',
    'canvas': {'width': W, 'height': H},
    'files': '<family>.svg/.png = clean drawing; <family>-labeled.svg/.png = with button labels. PNG files are 800x1040 with a transparent background.',
    'slotMapping': 'Key positions are physical. Except for G502 LIGHTSPEED (407F), they are not yet mapped to onboard-profile button slots.',
    'families': [],
    'models': {},
    'pending': PENDING,
}
for f in FAMILIES:
    for labeled in (False, True):
        name = f['id'] + ('-labeled' if labeled else '')
        with open(os.path.join(OUT, name + '.svg'), 'w', encoding='utf-8', newline='\n') as handle:
            handle.write(draw(f, labeled))
    keys = []
    for k in f['keys']:
        cx, cy = shape_center(k)
        keys.append({'id': k['id'], 'zh': k['zh'], 'en': k['en'], 'label': k['label'], 'programmable': k['programmable'],
                     'x': round(cx / W, 4), 'y': round(cy / H, 4)})
    manifest['families'].append({'id': f['id'], 'title': f['title'], 'models': f['models'], 'wpids': f['wpids'], 'reference': f['source'], 'keys': keys})
    for wpid in f['wpids']:
        manifest['models'][wpid] = f['id']
with open(os.path.join(OUT, 'manifest.json'), 'w', encoding='utf-8', newline='\n') as handle:
    json.dump(manifest, handle, ensure_ascii=False, indent=2)
print(len(FAMILIES), 'families,', len(manifest['models']), 'models')
