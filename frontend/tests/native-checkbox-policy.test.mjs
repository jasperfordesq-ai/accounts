import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import test from "node:test";
import ts from "typescript";

const sourceRoot = fileURLToPath(new URL("../src", import.meta.url));

test("every native accountant-facing checkbox uses the verified workbench boundary", () => {
  const failures = [];

  for (const file of tsxFiles(sourceRoot)) {
    const source = readFileSync(file, "utf8");
    const tree = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
    visit(tree, (node) => {
      if (!ts.isJsxSelfClosingElement(node) || node.tagName.getText(tree) !== "input") return;
      const type = stringAttribute(node, "type", tree);
      if (type !== "checkbox") return;

      const className = stringAttribute(node, "className", tree);
      if (!className?.split(/\s+/).includes("workbench-checkbox")) {
        const position = tree.getLineAndCharacterOfPosition(node.getStart(tree));
        failures.push(`${file}:${position.line + 1}`);
      }
    });
  }

  assert.deepEqual(failures, [], `Native checkboxes without workbench-checkbox: ${failures.join(", ")}`);
});

function tsxFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = `${directory}/${entry.name}`;
    if (entry.isDirectory()) return tsxFiles(path);
    return entry.isFile() && entry.name.endsWith(".tsx") ? [path] : [];
  });
}

function visit(node, action) {
  action(node);
  node.forEachChild((child) => visit(child, action));
}

function stringAttribute(element, name, tree) {
  const attribute = element.attributes.properties.find((candidate) =>
    ts.isJsxAttribute(candidate) && candidate.name.getText(tree) === name);
  if (!attribute || !ts.isJsxAttribute(attribute) || !attribute.initializer) return null;
  return ts.isStringLiteral(attribute.initializer) ? attribute.initializer.text : null;
}
