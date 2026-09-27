import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Keep version-specific sample-project dependencies out of package compatibility tests.
const [outputPath, unityVersion] = process.argv.slice(2);
if (!outputPath || !/^6000\.\d+\.\d+f\d+$/.test(unityVersion ?? '')) {
  throw new Error('Usage: node .github/scripts/create-unity-test-project.mjs <new-project-path> <6000.x.yfN>');
}

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const project = resolve(outputPath);
const packages = join(project, 'Packages');
const packageName = 'com.strangeape.open-unity-mcp';
const packagePath = relative(packages, join(repository, 'Packages', packageName)).replaceAll('\\', '/');
mkdirSync(dirname(project), { recursive: true });
mkdirSync(project); // Refuse to overwrite an existing project.
for (const folder of ['Assets', 'Packages', 'ProjectSettings']) {
  mkdirSync(join(project, folder));
}

const manifest = {
  dependencies: {
    [packageName]: `file:${packagePath}`,
    'com.unity.test-framework': '1.6.0',
    'com.unity.modules.imageconversion': '1.0.0',
    'com.unity.modules.physics': '1.0.0',
    'com.unity.modules.uielements': '1.0.0',
  },
  testables: [packageName],
};
writeFileSync(join(packages, 'manifest.json'), JSON.stringify(manifest, null, 2) + '\n');
writeFileSync(join(project, 'ProjectSettings', 'ProjectVersion.txt'), `m_EditorVersion: ${unityVersion}\n`);
console.log(`Created Unity ${unityVersion} package test project: ${project}`);
