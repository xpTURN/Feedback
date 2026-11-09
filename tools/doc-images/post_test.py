#!/usr/bin/env python3
# post_test.py — post.py on made-up images: the halving, the comparison's threshold (a pixel moved is the same image, a
# letter changed is not) and a GIF's frames and times. Run: python3 tools/doc-images/post_test.py
import os, sys, tempfile, unittest

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import post  # noqa: E402


# the editor's text at one pixel per point is about 12 px high
FONT = ImageFont.load_default(size=12)


def panel(text, size=(320, 120)):
    img = Image.new("RGB", size, (56, 56, 56))
    d = ImageDraw.Draw(img)
    d.rectangle((10, 10, 300, 40), fill=(80, 80, 80))
    d.text((16, 18), text, fill=(220, 220, 220), font=FONT)
    return img


class PostTests(unittest.TestCase):
    def test_halving(self):
        img = Image.new("RGB", (840, 1628), (10, 20, 30))
        self.assertEqual(post.one_x(img, 2).size, (420, 814))
        self.assertEqual(post.one_x(img, 1).size, (840, 1628))

    def test_same_image(self):
        self.assertEqual(post.differing(panel("Amplitude"), panel("Amplitude")), 0)

    def test_a_moved_pixel_is_the_same_image(self):
        a = panel("Amplitude")
        b = a.copy()
        b.putpixel((200, 100), (255, 255, 255))
        self.assertEqual(post.differing(a, b), 0)
        self.assertTrue(post.same(a, b))

    def test_scattered_pixels_are_the_same_image(self):
        a = panel("Amplitude")
        b = a.copy()
        for x in range(20, 300, 7):
            b.putpixel((x, 100), (255, 255, 255))
        self.assertTrue(post.same(a, b))

    def test_a_changed_letter_is_another_image(self):
        for one, other in (("Amplitude", "Amplitudo"), ("Amplitude", "Amplltude"), ("0.25 s", "0.20 s"), ("Delay", "Delay.")):
            a, b = panel(one), panel(other)
            self.assertGreater(post.differing(a, b), post.PIXELS, f"{one} · {other}")
            self.assertFalse(post.same(a, b), f"{one} · {other}")

    def test_a_faint_change_is_the_same_image(self):
        a = panel("Amplitude")
        b = Image.eval(a, lambda v: min(255, v + post.CHANNEL // 2))
        self.assertTrue(post.same(a, b))

    def test_sizes_differ(self):
        self.assertIsNone(post.differing(panel("x"), panel("x", (320, 121))))
        self.assertFalse(post.same(panel("x"), panel("x", (320, 121))))

    def test_reduced_keeps_the_letters(self):
        a, b = panel("Amplitude"), panel("Amplitudo")
        self.assertFalse(post.same(post.reduced(a), post.reduced(b)))
        self.assertTrue(post.same(post.reduced(a), a))

    def test_gif_frames_and_times(self):
        with tempfile.TemporaryDirectory() as t:
            frames = [(panel(f"frame {i}"), 0.04 if i < 2 else 0.6) for i in range(3)]
            one, two, other = (os.path.join(t, n) for n in ("a.gif", "b.gif", "c.gif"))
            post.write_gif(frames, one)
            post.write_gif(frames, two)
            post.write_gif(frames[:2] + [(panel("frame 9"), 0.6)], other)
            read = post.frames_of(one)
            self.assertEqual([d for _, d in read], [40, 40, 600])
            self.assertTrue(post.same_gif(one, two))
            self.assertFalse(post.same_gif(one, other))


if __name__ == "__main__":
    unittest.main()
