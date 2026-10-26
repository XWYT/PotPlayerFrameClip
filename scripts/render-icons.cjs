// Build-only dependency: npm install --prefix <temp> sharp@0.33.5 lucide-static@0.468.0
const fs = require('fs');
const path = require('path');
const modules = path.resolve(process.argv[2]);
const sharp = require(path.join(modules, 'sharp'));
const source = path.join(modules, 'lucide-static');
const output = path.resolve(__dirname, '../assets/icons');
fs.mkdirSync(output, {recursive: true});
const names = ['x', 'settings', 'folder-open', 'list-video', 'chevron-up', 'check',
  'triangle-alert', 'rotate-ccw', 'pin', 'pin-off', 'file-text', 'copy', 'minus', 'square', 'clapperboard',
  'scan-line', 'camera', 'film', 'image', 'check-check', 'list-filter', 'sliders-horizontal', 'chevron-down', 'plus', 'leaf'];
(async () => {
  for (const name of names) {
    const svg = fs.readFileSync(path.join(source, 'icons', name + '.svg'), 'utf8');
    for (const [theme, color] of [['light', '#252a28'], ['dark', '#f2f5f3']]) {
      const sizes = name === 'square' || name === 'copy' ? [10, 14, 15, 20, 21, 28, 30, 40] : [14, 20, 21, 28, 30, 40];
      for (const size of sizes) {
        await sharp(Buffer.from(svg.replaceAll('currentColor', color)))
          .resize(size, size).png().toFile(path.join(output, `${name}-${size}-${theme}.png`));
      }
    }
  }
  fs.copyFileSync(path.join(source, 'LICENSE'), path.resolve(__dirname, '../assets/LUCIDE-LICENSE'));
})();
