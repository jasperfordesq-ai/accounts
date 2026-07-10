import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const frontendRoot = fileURLToPath(new URL("..", import.meta.url));
const sourceRoot = path.join(frontendRoot, "src");
const protectedDirectorLoanFiles = new Set([
  "components/DirectorLoanEvidenceForm.tsx",
  "components/DirectorLoansManager.tsx",
  "components/period/YearEndDirectorLoanComplianceSummary.tsx",
]);

describe("critical journey accessibility semantics", () => {
  it("does not nest links and buttons", async () => {
    const offenders = [];

    for (const file of await tsxSourceFiles()) {
      const sourceFile = await parseSource(file);
      visit(sourceFile, (node) => {
        if (!isInteractive(node)) return;
        const outer = tagName(node);
        const nested = descendants(node).find((candidate) =>
          candidate !== node && isInteractive(candidate) && interactionKind(candidate) !== interactionKind(node));
        if (nested) offenders.push(location(sourceFile, node, `${outer} contains ${tagName(nested)}`));
      });
    }

    assert.deepEqual(offenders, [], `nested link/button controls:\n${offenders.join("\n")}`);
  });

  it("associates visual labels and gives native and HeroUI form controls an accessible name", async () => {
    const labelOffenders = [];
    const controlOffenders = [];

    for (const file of await tsxSourceFiles()) {
      const relative = path.relative(sourceRoot, file).replaceAll("\\", "/");
      if (protectedDirectorLoanFiles.has(relative)) continue;

      const sourceFile = await parseSource(file);
      const labelTargets = new Set();
      visit(sourceFile, (node) => {
        if (!isJsxTag(node, "label")) return;
        const htmlFor = attributeValue(opening(node), "htmlFor");
        if (htmlFor) labelTargets.add(htmlFor);

        const wrapsControl = descendants(node).some((candidate) =>
          isOneOfJsxTags(candidate, ["input", "select", "textarea", "Input", "Select", "Textarea"]));
        const delegatesChildren = descendants(node).some((candidate) =>
          ts.isJsxExpression(candidate) && ts.isIdentifier(candidate.expression) && candidate.expression.text === "children");
        if (!htmlFor && !wrapsControl && !delegatesChildren) {
          labelOffenders.push(location(sourceFile, node, "label has no htmlFor or wrapped control"));
        }
      });

      visit(sourceFile, (node) => {
        if (!isOneOfJsxTags(node, ["input", "select", "textarea", "Input"])) return;
        const element = opening(node);
        if (["input", "Input"].includes(tagName(node)) && attributeValue(element, "type") === "hidden") return;

        const hasAriaName = hasAttribute(element, "aria-label") || hasAttribute(element, "aria-labelledby");
        const id = attributeValue(element, "id");
        const hasExternalLabel = Boolean(id && labelTargets.has(id));
        const hasWrappingLabel = hasAncestor(node, (candidate) => isJsxTag(candidate, "label"));
        const hasLabelDelegatingWrapper = hasAncestor(node, (candidate) =>
          isOneOfJsxTags(candidate, ["Field", "FilterField", "BankFormField"]));
        const textField = findAncestor(node, (candidate) => isJsxTag(candidate, "TextField"));
        const hasTextFieldLabel = Boolean(textField && descendants(textField).some((candidate) =>
          isOneOfJsxTags(candidate, ["Label", "label"])));

        if (!hasAriaName && !hasExternalLabel && !hasWrappingLabel && !hasLabelDelegatingWrapper && !hasTextFieldLabel) {
          controlOffenders.push(location(sourceFile, node, `${tagName(node)} has no accessible label`));
        }
      });
    }

    assert.deepEqual(labelOffenders, [], `unassociated labels:\n${labelOffenders.join("\n")}`);
    assert.deepEqual(controlOffenders, [], `unnamed native controls:\n${controlOffenders.join("\n")}`);
  });

  it("keeps button names stable while loading or rendering icons", async () => {
    const offenders = [];

    for (const file of await tsxSourceFiles()) {
      const relative = path.relative(sourceRoot, file).replaceAll("\\", "/");
      if (protectedDirectorLoanFiles.has(relative)) continue;

      const sourceFile = await parseSource(file);
      visit(sourceFile, (node) => {
        if (!isOneOfJsxTags(node, ["button", "Button"])) return;
        const element = opening(node);
        const ariaNamed = hasAttribute(element, "aria-label") || hasAttribute(element, "aria-labelledby");
        if (!ariaNamed && !jsxChildren(node).some(expressionAlwaysHasText)) {
          offenders.push(location(sourceFile, node, `${tagName(node)} can render without an accessible name`));
        }
      });
    }

    assert.deepEqual(offenders, [], `unstably named buttons:\n${offenders.join("\n")}`);
  });

  it("keeps visible button wording inside explicit accessible names", async () => {
    const offenders = [];

    for (const file of await tsxSourceFiles()) {
      const relative = path.relative(sourceRoot, file).replaceAll("\\", "/");
      if (protectedDirectorLoanFiles.has(relative)) continue;

      const sourceFile = await parseSource(file);
      visit(sourceFile, (node) => {
        if (!isOneOfJsxTags(node, ["button", "Button"])) return;
        const ariaLabel = stringAttributeValue(opening(node), "aria-label");
        if (!ariaLabel) return;

        const accessibleName = normaliseName(ariaLabel);
        for (const visibleText of visibleTextLabels(node)) {
          const visibleName = normaliseName(visibleText);
          if (visibleName.length > 1 && !accessibleName.includes(visibleName)) {
            offenders.push(location(
              sourceFile,
              node,
              `aria-label "${ariaLabel}" omits visible text "${visibleText}"`,
            ));
          }
        }
      });
    }

    assert.deepEqual(offenders, [], `WCAG 2.5.3 label-in-name mismatches:\n${offenders.join("\n")}`);
  });

  it("uses the HeroUI v3 checkbox compound control instead of text-only roots", async () => {
    const offenders = [];

    for (const file of await tsxSourceFiles()) {
      const sourceFile = await parseSource(file);
      visit(sourceFile, (node) => {
        if (!isJsxTag(node, "Checkbox")) return;
        if (!descendants(node).some((candidate) => isJsxTag(candidate, "Checkbox.Content"))) {
          offenders.push(location(sourceFile, node, "Checkbox is missing Checkbox.Content"));
        }
      });
    }

    assert.deepEqual(offenders, [], `non-interactive HeroUI checkbox roots:\n${offenders.join("\n")}`);
  });

  it("retains modal focus management and the custom tab keyboard contract", async () => {
    const modal = await readFile(path.join(sourceRoot, "components", "ConfirmModal.tsx"), "utf8");
    const charityTabs = await readFile(
      path.join(sourceRoot, "app", "companies", "[companyId]", "periods", "[periodId]", "charity", "page.tsx"),
      "utf8",
    );

    for (const marker of ['aria-modal="true"', "aria-labelledby", "aria-describedby", "openModalStack"]) {
      assert.ok(modal.includes(marker), `ConfirmModal should retain ${marker}`);
    }
    assert.match(modal, /event\.key\s*[!=]==?\s*"Tab"/, "ConfirmModal should handle Tab navigation");
    assert.match(modal, /event\.key\s*===\s*"Escape"/, "ConfirmModal should handle Escape");
    assert.match(modal, /previouslyFocusedRef\.current\.focus\(\)/, "ConfirmModal should restore trigger focus");

    for (const marker of [
      'role="tablist"',
      'role="tab"',
      'role="tabpanel"',
      "aria-selected",
      'event.key === "ArrowRight"',
      'event.key === "ArrowLeft"',
      'event.key === "Home"',
      'event.key === "End"',
    ]) {
      assert.ok(charityTabs.includes(marker), `charity tabs should retain ${marker}`);
    }
  });
});

async function tsxSourceFiles() {
  return (await sourceFiles(sourceRoot)).filter((file) => file.endsWith(".tsx"));
}

async function sourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const nested = await Promise.all(entries.map(async (entry) => {
    const item = path.join(directory, entry.name);
    return entry.isDirectory() ? sourceFiles(item) : [item];
  }));
  return nested.flat();
}

async function parseSource(file) {
  return ts.createSourceFile(file, await readFile(file, "utf8"), ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
}

function visit(node, callback) {
  callback(node);
  ts.forEachChild(node, (child) => visit(child, callback));
}

function descendants(node) {
  const result = [];
  ts.forEachChild(node, (child) => {
    result.push(child);
    result.push(...descendants(child));
  });
  return result;
}

function opening(node) {
  if (ts.isJsxElement(node)) return node.openingElement;
  if (ts.isJsxSelfClosingElement(node)) return node;
  return undefined;
}

function tagName(node) {
  const element = opening(node);
  return element?.tagName.getText() ?? "";
}

function isJsxTag(node, name) {
  return (ts.isJsxElement(node) || ts.isJsxSelfClosingElement(node)) && tagName(node) === name;
}

function isOneOfJsxTags(node, names) {
  return names.includes(tagName(node));
}

function interactionKind(node) {
  return ["a", "Link", "ActionLink"].includes(tagName(node)) ? "link" : "button";
}

function isInteractive(node) {
  return isOneOfJsxTags(node, ["a", "Link", "ActionLink", "button", "Button"]);
}

function hasAttribute(element, name) {
  return Boolean(element?.attributes.properties.some((attribute) =>
    ts.isJsxAttribute(attribute) && attribute.name.getText() === name));
}

function attributeValue(element, name) {
  const attribute = element?.attributes.properties.find((candidate) =>
    ts.isJsxAttribute(candidate) && candidate.name.getText() === name);
  if (!attribute || !ts.isJsxAttribute(attribute) || !attribute.initializer) return undefined;
  if (ts.isStringLiteral(attribute.initializer)) return attribute.initializer.text;
  if (ts.isJsxExpression(attribute.initializer) && attribute.initializer.expression) {
    return attribute.initializer.expression.getText();
  }
  return undefined;
}

function stringAttributeValue(element, name) {
  const attribute = element?.attributes.properties.find((candidate) =>
    ts.isJsxAttribute(candidate) && candidate.name.getText() === name);
  return attribute && ts.isJsxAttribute(attribute) && attribute.initializer && ts.isStringLiteral(attribute.initializer)
    ? attribute.initializer.text
    : undefined;
}

function hasAncestor(node, predicate) {
  return Boolean(findAncestor(node, predicate));
}

function findAncestor(node, predicate) {
  let current = node.parent;
  while (current) {
    if (predicate(current)) return current;
    current = current.parent;
  }
  return undefined;
}

function jsxChildren(node) {
  return ts.isJsxElement(node) ? node.children : [];
}

function expressionAlwaysHasText(node) {
  if (ts.isJsxText(node)) return node.text.trim().length > 0;
  if (ts.isJsxExpression(node)) return node.expression ? expressionAlwaysHasText(node.expression) : false;
  if (ts.isJsxFragment(node) || ts.isJsxElement(node)) return node.children.some(expressionAlwaysHasText);
  if (ts.isJsxSelfClosingElement(node)) return false;
  if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isNonNullExpression(node)) {
    return expressionAlwaysHasText(node.expression);
  }
  if (ts.isConditionalExpression(node)) {
    return expressionAlwaysHasText(node.whenTrue) && expressionAlwaysHasText(node.whenFalse);
  }
  if (ts.isBinaryExpression(node)) {
    if (node.operatorToken.kind === ts.SyntaxKind.AmpersandAmpersandToken) return false;
    if (node.operatorToken.kind === ts.SyntaxKind.BarBarToken || node.operatorToken.kind === ts.SyntaxKind.QuestionQuestionToken) {
      return expressionAlwaysHasText(node.right);
    }
    return expressionAlwaysHasText(node.left) || expressionAlwaysHasText(node.right);
  }
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) || ts.isTemplateExpression(node)) {
    return node.getText().replaceAll(/[\s`'\"]/g, "").length > 0;
  }
  if (ts.isNumericLiteral(node) || ts.isCallExpression(node) || ts.isPropertyAccessExpression(node) || ts.isElementAccessExpression(node)) {
    return true;
  }
  if (ts.isIdentifier(node)) return !["false", "null", "undefined"].includes(node.text);
  return false;
}

function visibleTextLabels(node) {
  if (ts.isJsxText(node)) return node.text.trim() ? [node.text.trim()] : [];
  if (ts.isJsxExpression(node)) return node.expression ? visibleTextLabels(node.expression) : [];
  if (ts.isJsxFragment(node) || ts.isJsxElement(node)) return node.children.flatMap(visibleTextLabels);
  if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node) || ts.isNonNullExpression(node)) {
    return visibleTextLabels(node.expression);
  }
  if (ts.isConditionalExpression(node)) {
    return [...visibleTextLabels(node.whenTrue), ...visibleTextLabels(node.whenFalse)];
  }
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return [node.text];
  return [];
}

function normaliseName(value) {
  return value.toLocaleLowerCase("en-IE").replaceAll(/\s+/g, " ").trim();
}

function location(sourceFile, node, message) {
  const position = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
  return `${path.relative(sourceRoot, sourceFile.fileName)}:${position.line + 1}:${position.character + 1} ${message}`;
}
