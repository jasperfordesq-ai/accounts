"use client";

import { use, useState, useEffect, useCallback, useMemo } from "react";
import Link from "next/link";
import {
  Button,
  Card,
  Chip,
  Spinner,
} from "@heroui/react";
import {
  ArrowLeft,
  RefreshCw,
  CheckCircle2,
  FileText,
  Plus,
  Trash2,
  Wand2,
  Save,
} from "lucide-react";
import { toast } from "sonner";
import { Breadcrumbs } from "@/components/Breadcrumbs";
import { PeriodWorkspaceSkeleton } from "@/components/Skeleton";
import {
  getCompany,
  getPeriod,
  getNotes,
  generateNotes,
  saveYearEndReviewConfirmation,
  updateNote,
  createNote,
  deleteNote,
  type Company,
  type AccountingPeriod,
  type NotesDisclosure,
} from "@/lib/api";
import { useUnsavedChanges } from "@/lib/useUnsavedChanges";
import { useDestructiveActionConfirmation } from "@/lib/useDestructiveAction";
import { ResourceStateNotice } from "@/components/ResourceStateNotice";
import { useAuth } from "@/components/AuthProvider";
import { ReadOnlyNotice } from "@/components/workbench";
import {
  INITIAL_RESOURCE_STATE,
  beginResourceLoad,
  canUseResourceAsEvidence,
  completeResourceLoad,
  failResourceLoad,
  loadResourceGroup,
  shouldRenderResourceEmpty,
  type ResourceState,
} from "@/lib/resourceState";

const inputClass =
  "w-full rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 shadow-sm transition-colors focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

const textareaClass =
  "w-full resize-y rounded-lg border border-[var(--control-border)] bg-white px-3 py-2 text-sm text-gray-900 shadow-sm transition-colors focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500 dark:bg-neutral-800 dark:text-gray-100";

const MANUAL_REVIEW_KEYS: Record<string, string> = {
  "DIRECTOR-REMUNERATION": "note-directors-remuneration",
  "ULTIMATE-CONTROLLING-PARTY": "note-ultimate-controlling-party",
  "FINANCIAL-INSTRUMENTS": "note-financial-instruments",
  "CAPITAL-COMMITMENTS": "note-capital-commitments",
  "DEFERRED-TAX": "note-deferred-tax",
};

export default function NotesPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);
  const { canWriteWorkingPapers } = useAuth();

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [notes, setNotes] = useState<NotesDisclosure[]>([]);
  const [shellState, setShellState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [notesState, setNotesState] = useState<ResourceState>(INITIAL_RESOURCE_STATE);
  const [generating, setGenerating] = useState(false);
  const [savingIds, setSavingIds] = useState<Set<number>>(new Set());
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const { requestDestructiveAction, destructiveActionConfirmation } = useDestructiveActionConfirmation();

  // New note form state
  const [showNewForm, setShowNewForm] = useState(false);
  const [newTitle, setNewTitle] = useState("");
  const [newContent, setNewContent] = useState("");
  const [creatingNote, setCreatingNote] = useState(false);

  // Local edits tracking (noteId -> edited content)
  const [editedContent, setEditedContent] = useState<Record<number, string>>({});
  const [reviewDrafts, setReviewDrafts] = useState<Record<string, string>>({});
  const [savingReviewCode, setSavingReviewCode] = useState<string | null>(null);

  const hasUnsavedEdits = useMemo(() => {
    const noteEditsDirty = Object.entries(editedContent).some(([noteId, content]) =>
      content !== (notes.find((note) => note.id === Number(noteId))?.content ?? ""));
    const reviewEditsDirty = Object.entries(reviewDrafts).some(([code, content]) =>
      content !== (notes.find((note) => note.code === code)?.content ?? ""));
    const newNoteDirty = showNewForm && (newTitle !== "" || newContent !== "");
    return noteEditsDirty || reviewEditsDirty || newNoteDirty;
  }, [editedContent, newContent, newTitle, notes, reviewDrafts, showNewForm]);
  useUnsavedChanges(hasUnsavedEdits);

  const loadShell = useCallback(async (onlyKeys?: string[]) => {
    const loaders = {
      company: () => getCompany(cId),
      period: () => getPeriod(cId, pId),
    };
    const keys = (onlyKeys ?? Object.keys(loaders)) as Array<keyof typeof loaders>;
    setShellState((current) => beginResourceLoad(current, current.hasRetainedData));
    const result = await loadResourceGroup(loaders, keys);
    if (result.values.company) setCompany(result.values.company);
    if (result.values.period) setPeriod(result.values.period);
    if (result.failedResourceKeys.length === 0) {
      setShellState(completeResourceLoad(false));
      return;
    }
    setShellState((current) => failResourceLoad({
      failedResourceKeys: result.failedResourceKeys,
      errors: result.errors,
    }, current.hasRetainedData || Object.keys(result.values).length > 0));
  }, [cId, pId]);

  const loadNotes = useCallback(async () => {
    setNotesState((current) => beginResourceLoad(current, current.hasRetainedData));
    try {
      const notesData = await getNotes(cId, pId);
      setNotes(notesData);
      setNotesState(completeResourceLoad(notesData.length === 0));
    } catch (loadError) {
      const message = loadError instanceof Error ? loadError.message : "Failed to load notes";
      setNotesState((current) => failResourceLoad({
        failedResourceKeys: ["notes"],
        errors: { notes: message },
      }, current.hasRetainedData));
    }
  }, [cId, pId]);

  const loadData = useCallback(async () => {
    await Promise.all([loadShell(), loadNotes()]);
  }, [loadNotes, loadShell]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  async function handleGenerateNotes() {
    setGenerating(true);
    try {
      const generatedNotes = await generateNotes(cId, pId);
      setNotes(generatedNotes);
      setNotesState(completeResourceLoad(generatedNotes.length === 0));
      setEditedContent({});
      toast.success(`Generated ${generatedNotes.length} note${generatedNotes.length !== 1 ? "s" : ""}`);
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to generate notes");
    } finally {
      setGenerating(false);
    }
  }

  async function handleSaveNote(note: NotesDisclosure) {
    const noteId = note.id!;
    setSavingIds((prev) => new Set(prev).add(noteId));
    try {
      const content = editedContent[noteId] ?? note.content;
      await updateNote(cId, pId, noteId, {
        ...note,
        content,
      });
      // Update local state
      setNotes((prev) =>
        prev.map((n) => (n.id === noteId ? { ...n, content } : n))
      );
      // Clear edited content for this note
      setEditedContent((prev) => {
        const next = { ...prev };
        delete next[noteId];
        return next;
      });
      toast.success("Note saved");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to save note");
    } finally {
      setSavingIds((prev) => {
        const next = new Set(prev);
        next.delete(noteId);
        return next;
      });
    }
  }

  async function handleToggleIncluded(note: NotesDisclosure) {
    const noteId = note.id!;
    setSavingIds((prev) => new Set(prev).add(noteId));
    try {
      const updated = await updateNote(cId, pId, noteId, {
        ...note,
        isIncluded: !note.isIncluded,
      });
      setNotes((prev) =>
        prev.map((n) => (n.id === noteId ? updated : n))
      );
      toast.success(updated.isIncluded ? "Note included" : "Note excluded");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to update note");
    } finally {
      setSavingIds((prev) => {
        const next = new Set(prev);
        next.delete(noteId);
        return next;
      });
    }
  }

  async function handleDeleteNote(noteId: number) {
    setDeletingId(noteId);
    try {
      await deleteNote(cId, pId, noteId);
      const nextNotes = notes.filter((note) => note.id !== noteId);
      setNotes(nextNotes);
      setNotesState(completeResourceLoad(nextNotes.length === 0));
      setEditedContent((prev) => {
        const next = { ...prev };
        delete next[noteId];
        return next;
      });
      toast.success("Note deleted");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete note");
      throw err;
    } finally {
      setDeletingId(null);
    }
  }

  async function handleCreateNote() {
    if (!newTitle.trim()) return;
    setCreatingNote(true);
    try {
      const maxNoteNumber = notes.reduce(
        (max, n) => Math.max(max, n.noteNumber),
        0
      );
      const created = await createNote(cId, pId, {
        noteNumber: maxNoteNumber + 1,
        title: newTitle.trim(),
        content: newContent.trim() || undefined,
        isRequired: false,
        isIncluded: true,
      });
      setNotes((prev) => [...prev, created]);
      setNotesState(completeResourceLoad(false));
      setNewTitle("");
      setNewContent("");
      setShowNewForm(false);
      toast.success("Custom note created");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to create note");
    } finally {
      setCreatingNote(false);
    }
  }

  async function handleSaveManualReview(note: NotesDisclosure) {
    if (!note.code || !MANUAL_REVIEW_KEYS[note.code]) return;
    const disclosure = (reviewDrafts[note.code] ?? note.content ?? "").trim();
    if (!disclosure) {
      toast.error("Enter the reviewed disclosure wording before recording manual evidence");
      return;
    }
    setSavingReviewCode(note.code);
    try {
      await saveYearEndReviewConfirmation(cId, pId, MANUAL_REVIEW_KEYS[note.code], {
        confirmed: true,
        note: disclosure,
      });
      const regenerated = await generateNotes(cId, pId);
      setNotes(regenerated);
      setReviewDrafts((current) => {
        const next = { ...current };
        delete next[note.code!];
        return next;
      });
      toast.success("Retained manual-review evidence recorded and checklist regenerated");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to record manual-review evidence");
    } finally {
      setSavingReviewCode(null);
    }
  }

  if (shellState.status === "loading" && !shellState.hasRetainedData) {
    return <PeriodWorkspaceSkeleton />;
  }

  if (!company || !period) {
    return (
      <div className="max-w-2xl mx-auto">
        <Card className="border border-red-200 dark:border-red-800 bg-white dark:bg-neutral-900">
          <Card.Content className="text-center py-8">
            <ResourceStateNotice state={shellState} label="company and period context" onRetry={() => loadShell(shellState.failedResourceKeys)} />
          </Card.Content>
        </Card>
      </div>
    );
  }

  const sortedNotes = [...notes].sort(
    (a, b) => a.noteNumber - b.noteNumber
  );
  const includedCount = notes.filter((n) => n.isIncluded).length;

  return (
    <div className="animate-fade-in">
      {/* Breadcrumbs */}
      <Breadcrumbs
        items={[
          { label: "Company", href: `/companies/${companyId}` },
          { label: "Period", href: `/companies/${companyId}/periods/${periodId}` },
          { label: "Notes" },
        ]}
      />

      <div className="mb-4 space-y-3">
        <ResourceStateNotice state={shellState} label="company and period context" onRetry={() => loadShell(shellState.failedResourceKeys)} />
        <ResourceStateNotice state={notesState} label="notes evidence" onRetry={loadNotes} />
      </div>

      {!canWriteWorkingPapers && <ReadOnlyNotice subject="financial-statement notes" />}

      {/* Header */}
      <div className="mb-6">
        <Link
          href={`/companies/${companyId}/periods/${periodId}`}
          className="inline-flex items-center gap-1.5 text-sm text-[var(--muted-foreground)] hover:text-[var(--accent)] mb-3"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Period Workspace
        </Link>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Notes to the Financial Statements
        </h1>
        {company && period && (
          <p className="text-sm text-[var(--muted-foreground)] mt-1">
            {company.legalName} &mdash;{" "}
            {new Date(period.periodStart).toLocaleDateString("en-IE")} to{" "}
            {new Date(period.periodEnd).toLocaleDateString("en-IE")}
          </p>
        )}
      </div>

      {/* Actions Bar */}
      <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 mb-6">
        <Card.Content className="p-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              {canWriteWorkingPapers && <Button
                variant="primary"
                onPress={handleGenerateNotes}
                isDisabled={generating || !canUseResourceAsEvidence(notesState)}
              >
                {generating ? (
                  <>
                    <Spinner size="sm" className="mr-2" />
                    Generating...
                  </>
                ) : (
                  <>
                    <Wand2 className="w-4 h-4 mr-1" />
                    Generate Auto-Notes
                  </>
                )}
              </Button>}
              {canWriteWorkingPapers && <Button
                variant="outline"
                size="sm"
                onPress={() => setShowNewForm(true)}
                isDisabled={!canUseResourceAsEvidence(notesState)}
              >
                <Plus className="w-4 h-4 mr-1" />
                Add Custom Note
              </Button>}
              <Button
                variant="ghost"
                size="sm"
                onPress={loadData}
                isDisabled={notesState.status === "loading" || notesState.status === "stale/retrying"}
              >
                <RefreshCw
                  className={`w-4 h-4 mr-1 ${notesState.status === "loading" || notesState.status === "stale/retrying" ? "animate-spin" : ""}`}
                />
                Refresh
              </Button>
            </div>
            <div className="flex items-center gap-3 text-sm text-[var(--muted-foreground)]">
              <span>
                {notes.length} note{notes.length !== 1 ? "s" : ""} total
              </span>
              <Chip size="sm" variant="soft" color="success">
                {includedCount} included
              </Chip>
            </div>
          </div>
        </Card.Content>
      </Card>

      {/* New Note Form */}
      {canWriteWorkingPapers && showNewForm && (
        <Card className="shadow-sm border border-emerald-200 dark:border-emerald-800 bg-emerald-50/20 dark:bg-emerald-900/10 mb-6 animate-fade-in">
          <Card.Header>
            <Card.Title>Add Custom Note</Card.Title>
          </Card.Header>
          <Card.Content>
            <div className="space-y-4">
              <div>
                <label
                  htmlFor="note-title"
                  className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
                >
                  Note Title
                </label>
                <input
                  id="note-title"
                  type="text"
                  value={newTitle}
                  onChange={(e) => setNewTitle(e.target.value)}
                  placeholder="e.g. Directors' Remuneration"
                  className={inputClass}
                />
              </div>
              <div>
                <label
                  htmlFor="note-content"
                  className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1"
                >
                  Content
                </label>
                <textarea
                  id="note-content"
                  value={newContent}
                  onChange={(e) => setNewContent(e.target.value)}
                  rows={4}
                  placeholder="Enter the note disclosure content..."
                  className={textareaClass}
                />
              </div>
              <div className="flex items-center gap-3">
                <Button
                  variant="primary"
                  size="sm"
                  aria-label="Create note"
                  onPress={handleCreateNote}
                  isDisabled={creatingNote || !newTitle.trim()}
                >
                  {creatingNote ? (
                    <Spinner size="sm" />
                  ) : (
                    <>
                      <Plus className="w-4 h-4 mr-1" />
                      Create Note
                    </>
                  )}
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  onPress={() => {
                    setShowNewForm(false);
                    setNewTitle("");
                    setNewContent("");
                  }}
                >
                  Cancel
                </Button>
              </div>
            </div>
          </Card.Content>
        </Card>
      )}

      {/* Notes List */}
      {shouldRenderResourceEmpty(notesState) ? (
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="text-center py-12">
            <FileText className="w-10 h-10 text-[var(--muted-foreground)] mx-auto mb-3" />
            <p className="text-sm text-[var(--muted-foreground)]">
              No notes have been created yet.
            </p>
            <p className="text-xs text-[var(--muted-foreground)] mt-1">
              Click &ldquo;Generate Auto-Notes&rdquo; to create standard
              disclosures, or add a custom note.
            </p>
          </Card.Content>
        </Card>
      ) : sortedNotes.length > 0 ? (
        <div className="space-y-4">
          {sortedNotes.map((note) => {
            const noteId = note.id!;
            const isSaving = savingIds.has(noteId);
            const isDeleting = deletingId === noteId;
            const hasLocalEdits = noteId in editedContent;
            const currentContent =
              editedContent[noteId] ?? note.content ?? "";

            return (
              <Card
                key={noteId}
                className={`shadow-sm border animate-fade-in bg-white dark:bg-neutral-900 ${
                  note.isIncluded
                    ? "border-gray-200 dark:border-neutral-700"
                    : "border-dashed border-gray-400 dark:border-neutral-500"
                }`}
              >
                <Card.Content className="p-5">
                  <div className="flex items-start justify-between gap-4 mb-3">
                    <div className="flex items-center gap-3">
                      <span className="inline-flex items-center justify-center w-7 h-7 rounded-full bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-400 text-xs font-bold">
                        {note.noteNumber}
                      </span>
                      <div>
                        <h3 className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                          {note.title}
                        </h3>
                        <div className="flex items-center gap-2 mt-0.5">
                          {note.code && (
                            <Chip size="sm" variant="soft" color="default">
                              {note.code}
                            </Chip>
                          )}
                          {note.isRequired && (
                            <Chip
                              size="sm"
                              variant="soft"
                              color="danger"
                            >
                              Required
                            </Chip>
                          )}
                          <Chip
                            size="sm"
                            variant="soft"
                            color={
                              note.isIncluded ? "success" : "default"
                            }
                          >
                            {note.isIncluded ? "Included" : "Excluded"}
                          </Chip>
                          {note.checklistState && (
                            <Chip
                              size="sm"
                              variant="soft"
                              color={
                                note.checklistState === "Required"
                                  ? "success"
                                  : note.checklistState === "NotApplicable"
                                    ? "default"
                                    : "warning"
                              }
                            >
                              {note.checklistState === "NotApplicable"
                                ? "Not applicable"
                                : note.checklistState === "ExplicitReview"
                                  ? "Explicit review"
                                  : "Required"}
                            </Chip>
                          )}
                        </div>
                      </div>
                    </div>
                    {canWriteWorkingPapers && <div className="flex items-center gap-2 shrink-0">
                      {/* Inclusion toggle */}
                      {!note.isRequired && <label className="flex items-center gap-2 cursor-pointer">
                        <input
                          type="checkbox"
                          checked={note.isIncluded}
                          onChange={() => handleToggleIncluded(note)}
                          disabled={isSaving}
                          title={note.isIncluded ? "Exclude this note from the financial statements" : "Include this note in the financial statements"}
                          className="rounded border-[var(--control-border)] text-emerald-600 focus:ring-emerald-500 dark:bg-neutral-800"
                        />
                        <span className="text-xs text-[var(--muted-foreground)]">
                          Include
                        </span>
                      </label>}
                      {/* Delete */}
                      {!note.isRequired && (
                        <Button
                          variant="ghost"
                          size="sm"
                          isIconOnly
                          aria-label={`Delete ${note.title} note`}
                          onPress={() => requestDestructiveAction({
                            recordLabel: `note "${note.title}"`,
                            consequence: `This permanently removes the note text${note.isIncluded ? " currently included in the financial statements" : ""} and its retained review state. The removal cannot be undone.`,
                            onConfirm: () => handleDeleteNote(noteId),
                            successAnnouncement: `Note ${note.title} was removed.`,
                          })}
                          isDisabled={isDeleting}
                        >
                          {isDeleting ? (
                            <Spinner size="sm" />
                          ) : (
                            <Trash2 className="w-4 h-4 text-red-600 hover:text-red-700 dark:text-red-400 dark:hover:text-red-300" />
                          )}
                        </Button>
                      )}
                    </div>}
                  </div>

                  {/* Content editor */}
                  {canWriteWorkingPapers && !note.isRequired ? (
                    <textarea
                      aria-label={`Edit ${note.title} disclosure content`}
                      value={currentContent}
                      onChange={(e) =>
                        setEditedContent((prev) => ({
                          ...prev,
                          [noteId]: e.target.value,
                        }))
                      }
                      rows={4}
                      className={textareaClass}
                      placeholder="Enter note content..."
                    />
                  ) : (
                    <p className="whitespace-pre-wrap text-sm leading-6 text-gray-700 dark:text-gray-300">
                      {currentContent || (
                        note.checklistState === "NotApplicable"
                          ? "No disclosure is rendered because this checklist item is not applicable."
                          : "This checklist item is waiting for retained review evidence or source data."
                      )}
                    </p>
                  )}

                  {note.reviewEvidence && (
                    <p className="mt-3 text-xs text-[var(--muted-foreground)]">
                      Review evidence: {note.reviewEvidence}
                    </p>
                  )}

                  {canWriteWorkingPapers && note.code && MANUAL_REVIEW_KEYS[note.code] && (
                    <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50/70 p-3 dark:border-amber-900 dark:bg-amber-950/20">
                      <p className="mb-2 text-xs font-semibold text-amber-900 dark:text-amber-200">
                        Manual disclosure handoff
                      </p>
                      <textarea
                        aria-label={`Record reviewed wording for ${note.title}`}
                        value={reviewDrafts[note.code] ?? note.content ?? ""}
                        onChange={(event) => setReviewDrafts((current) => ({
                          ...current,
                          [note.code!]: event.target.value,
                        }))}
                        rows={3}
                        className={textareaClass}
                        placeholder="Paste the disclosure wording reviewed against retained working papers"
                      />
                      <Button
                        className="mt-2"
                        variant="outline"
                        size="sm"
                        aria-label={`Record review evidence for ${note.title}`}
                        onPress={() => handleSaveManualReview(note)}
                        isDisabled={savingReviewCode === note.code}
                      >
                        {savingReviewCode === note.code ? <Spinner size="sm" /> : "Record review evidence"}
                      </Button>
                    </div>
                  )}

                  {/* Save button - only visible when edits exist */}
                  {canWriteWorkingPapers && hasLocalEdits && (
                    <div className="mt-2 flex items-center gap-2">
                      <Button
                        variant="primary"
                        size="sm"
                        onPress={() => handleSaveNote(note)}
                        isDisabled={isSaving}
                      >
                        {isSaving ? (
                          <>
                            <Spinner size="sm" className="mr-1" />
                            Saving...
                          </>
                        ) : (
                          <>
                            <Save className="w-4 h-4 mr-1" />
                            Save Changes
                          </>
                        )}
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onPress={() =>
                          setEditedContent((prev) => {
                            const next = { ...prev };
                            delete next[noteId];
                            return next;
                          })
                        }
                      >
                        Discard
                      </Button>
                      <CheckCircle2 className="w-4 h-4 text-amber-500" />
                      <span className="text-xs text-amber-800 dark:text-amber-300">
                        Unsaved changes
                      </span>
                    </div>
                  )}
                </Card.Content>
              </Card>
            );
          })}
        </div>
      ) : null}
      {destructiveActionConfirmation}
    </div>
  );
}
