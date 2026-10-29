"""Writes three small original sample books (EPUB, Romanian TXT, Markdown) for testing and README screenshots.

Usage: python scripts/make_sample_books.py <output-folder>
The text is original to this project and may be used freely.
"""
import os
import sys
import zipfile

out = sys.argv[1] if len(sys.argv) > 1 else "sample-books"
os.makedirs(out, exist_ok=True)

SOIL = [
    ("Living soil", "Healthy soil is a community. Earthworms, fungi and bacteria break down organic matter and release nutrients slowly. "
     "Digging disturbs this community, so many gardeners prefer to add compost on the surface and let worms carry it down. "
     "A handful of good soil should crumble easily and smell sweet and earthy."),
    ("Compost basics", "Compost needs a balance of nitrogen-rich green material, such as kitchen scraps and grass clippings, and carbon-rich brown "
     "material, such as dry leaves and straw. Keep the heap as moist as a wrung-out sponge and turn it occasionally to add air. "
     "A finished compost is dark, crumbly and smells like a forest floor. Avoid adding meat, dairy or diseased plants."),
    ("Mulching", "A layer of mulch protects the soil surface, keeps moisture in the ground and suppresses weeds. Straw, leaf mould and wood chips "
     "are common choices. Keep mulch a few centimetres away from stems and tree trunks to prevent rot. "
     "Mulch also moderates soil temperature, which helps seedlings through hot summers."),
    ("Water harvesting", "Rainwater stored in barrels and tanks is soft, free and welcomed by plants. Swales and ponds slow runoff and let water "
     "sink into the ground. Mulch and compost increase the amount of water that soil can hold."),
]

EPUB_PATH = os.path.join(out, "Living Soil - A Gardener's Primer.epub")
with zipfile.ZipFile(EPUB_PATH, "w") as z:
    z.writestr("mimetype", "application/epub+zip", compress_type=zipfile.ZIP_STORED)
    z.writestr("META-INF/container.xml",
               "<?xml version='1.0'?><container xmlns='urn:oasis:names:tc:opendocument:xmlns:container' version='1.0'><rootfiles>"
               "<rootfile full-path='OEBPS/content.opf' media-type='application/oebps-package+xml'/></rootfiles></container>")
    items = "".join(f"<item id='c{i}' href='ch{i}.xhtml' media-type='application/xhtml+xml'/>" for i in range(len(SOIL)))
    spine = "".join(f"<itemref idref='c{i}'/>" for i in range(len(SOIL)))
    z.writestr("OEBPS/content.opf",
               "<?xml version='1.0'?><package xmlns='http://www.idpf.org/2007/opf' version='3.0'><metadata xmlns:dc='http://purl.org/dc/elements/1.1/'>"
               "<dc:title>Living Soil: A Gardener's Primer</dc:title><dc:creator>Garden Helper sample</dc:creator><dc:language>en</dc:language>"
               "<dc:subject>soil</dc:subject><dc:subject>permaculture</dc:subject><dc:description>A short sample book about soil, compost and mulch.</dc:description></metadata>"
               f"<manifest>{items}<item id='ncx' href='toc.ncx' media-type='application/x-dtbncx+xml'/></manifest><spine>{spine}</spine></package>")
    navpoints = "".join(f"<navPoint id='n{i}'><navLabel><text>{t}</text></navLabel><content src='ch{i}.xhtml'/></navPoint>" for i, (t, _) in enumerate(SOIL))
    z.writestr("OEBPS/toc.ncx", f"<?xml version='1.0'?><ncx xmlns='http://www.daisy.org/z3986/2005/ncx/'><navMap>{navpoints}</navMap></ncx>")
    for i, (title, body) in enumerate(SOIL):
        z.writestr(f"OEBPS/ch{i}.xhtml",
                   f"<html xmlns='http://www.w3.org/1999/xhtml'><head><title>{title}</title></head><body><h1>{title}</h1><p>{body}</p></body></html>")

RO = """Ghid practic de compostare

Ce este compostul
Compostul este un material întunecat și afânat, obținut prin descompunerea resturilor vegetale. Îmbunătățește structura solului, ține apa în pământ și hrănește viața din sol.

Ce punem în grămadă
Un compost echilibrat are nevoie de materiale bogate în azot, precum resturi de bucătărie și iarbă proaspăt tăiată, și de materiale bogate în carbon, precum frunze uscate, paie și carton. Evită carnea, lactatele și plantele bolnave.

Umiditate și aer
Grămada trebuie să fie umedă ca un burete stors. Dacă miroase urât, este prea ud sau lipsit de aer: amestec-o și adaugă material uscat. Sămânța buruienilor poate supraviețui într-un compost rece, de aceea materialul cu semințe se tratează cu grijă.

Când este gata
Compostul copt este brun-închis, se sfărâmă ușor și miroase a pădure. De obicei durează de la câteva luni la un an. Se răspândește în strat subțire primăvara sau toamna.
"""
with open(os.path.join(out, "Ghid de compostare.txt"), "w", encoding="utf-8") as f:
    f.write(RO)

MD = """# Seed saving notes

## Isolation
Some crops cross-pollinate easily and need distance or barriers to stay true to type. Self-pollinating plants such as peas and tomatoes are the easiest place to start.

## Hybrids
Seed saved from a hybrid variety usually does not reproduce the parent plant. Choose open-pollinated varieties if you want to save seed.

## Drying and storage
Dry seed thoroughly before storing it in a cool, dark, dry place. Label every packet with the variety and the year.
"""
with open(os.path.join(out, "Seed saving notes.md"), "w", encoding="utf-8") as f:
    f.write(MD)
print("Wrote sample books to", out)
