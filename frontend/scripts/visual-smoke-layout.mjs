const DEFAULT_TOLERANCE = 2;
const DEFAULT_MIN_OVERLAP_AREA = 12;
const MAX_TEXT_PREVIEW = 48;

export function findOverlappingTextBlocks(blocks, options = {}) {
  const tolerance = options.tolerance ?? DEFAULT_TOLERANCE;
  const minOverlapArea = options.minOverlapArea ?? DEFAULT_MIN_OVERLAP_AREA;
  const normalizedBlocks = uniqueBlocks(blocks
    .map(normalizeBlock)
    .filter((block) => block && block.text.length > 0 && block.rect.width > 0 && block.rect.height > 0));
  const issues = [];

  for (let firstIndex = 0; firstIndex < normalizedBlocks.length; firstIndex += 1) {
    for (let secondIndex = firstIndex + 1; secondIndex < normalizedBlocks.length; secondIndex += 1) {
      const first = normalizedBlocks[firstIndex];
      const second = normalizedBlocks[secondIndex];
      const overlapWidth = Math.min(first.rect.right, second.rect.right) - Math.max(first.rect.left, second.rect.left);
      const overlapHeight = Math.min(first.rect.bottom, second.rect.bottom) - Math.max(first.rect.top, second.rect.top);

      if (overlapWidth <= tolerance || overlapHeight <= tolerance) continue;

      const overlapArea = Math.round(overlapWidth * overlapHeight);
      if (overlapArea < minOverlapArea) continue;
      if (isNearDuplicateTextBlock(first, second, overlapArea)) continue;

      issues.push({
        first: first.label,
        second: second.label,
        overlapArea,
        message: `${first.label} overlaps ${second.label} by ${Math.round(overlapWidth)}x${Math.round(overlapHeight)}px (${preview(first.text)} / ${preview(second.text)})`,
      });
    }
  }

  return issues;
}

export function formatLayoutIssues(routeName, issues) {
  if (!issues.length) return "";
  return [
    `${routeName} has text layout overlap:`,
    ...issues.map((issue, index) => `${index + 1}. ${issue.message}`),
  ].join("\n");
}

function isNearDuplicateTextBlock(first, second, overlapArea) {
  if (first.text.toLowerCase() !== second.text.toLowerCase()) return false;

  const firstArea = first.rect.width * first.rect.height;
  const secondArea = second.rect.width * second.rect.height;
  const smallerArea = Math.min(firstArea, secondArea);
  if (smallerArea <= 0) return false;

  return overlapArea / smallerArea >= 0.8;
}

function normalizeBlock(block) {
  if (!block || typeof block !== "object") return null;
  const text = normalizeText(block.text);
  const rect = normalizeRect(block.rect);
  if (!rect) return null;

  return {
    label: normalizeText(block.label) || "text block",
    text,
    rect,
  };
}

function normalizeRect(rect) {
  if (!rect || typeof rect !== "object") return null;

  const left = numberOrNull(rect.left);
  const top = numberOrNull(rect.top);
  const width = numberOrNull(rect.width);
  const height = numberOrNull(rect.height);
  const right = numberOrNull(rect.right) ?? (left !== null && width !== null ? left + width : null);
  const bottom = numberOrNull(rect.bottom) ?? (top !== null && height !== null ? top + height : null);

  if (left === null || top === null || right === null || bottom === null) return null;

  return {
    left,
    top,
    right,
    bottom,
    width: width ?? right - left,
    height: height ?? bottom - top,
  };
}

function uniqueBlocks(blocks) {
  const seen = new Set();
  const unique = [];

  for (const block of blocks) {
    const key = [
      block.text.toLowerCase(),
      Math.round(block.rect.left),
      Math.round(block.rect.top),
      Math.round(block.rect.right),
      Math.round(block.rect.bottom),
    ].join("|");

    if (seen.has(key)) continue;
    seen.add(key);
    unique.push(block);
  }

  return unique;
}

function normalizeText(value) {
  return typeof value === "string" ? value.replace(/\s+/g, " ").trim() : "";
}

function numberOrNull(value) {
  return typeof value === "number" && Number.isFinite(value) ? value : null;
}

function preview(text) {
  const normalized = normalizeText(text);
  return normalized.length > MAX_TEXT_PREVIEW ? `${normalized.slice(0, MAX_TEXT_PREVIEW - 1)}...` : normalized;
}
