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
  AlertTriangle,
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
  updateNote,
  createNote,
  deleteNote,
  type Company,
  type AccountingPeriod,
  type NotesDisclosure,
} from "@/lib/api";

const inputClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 shadow-sm focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500 transition-colors";

const textareaClass =
  "w-full rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 px-3 py-2 text-sm text-gray-900 dark:text-gray-100 shadow-sm focus:border-emerald-500 focus:outline-none focus:ring-1 focus:ring-emerald-500 resize-y transition-colors";

export default function NotesPage({
  params,
}: {
  params: Promise<{ companyId: string; periodId: string }>;
}) {
  const { companyId, periodId } = use(params);
  const cId = Number(companyId);
  const pId = Number(periodId);

  const [company, setCompany] = useState<Company | null>(null);
  const [period, setPeriod] = useState<AccountingPeriod | null>(null);
  const [notes, setNotes] = useState<NotesDisclosure[]>([]);
  const [loading, setLoading] = useState(true);
  const [generating, setGenerating] = useState(false);
  const [savingIds, setSavingIds] = useState<Set<number>>(new Set());
  const [deletingId, setDeletingId] = useState<number | null>(null);

  // New note form state
  const [showNewForm, setShowNewForm] = useState(false);
  const [newTitle, setNewTitle] = useState("");
  const [newContent, setNewContent] = useState("");
  const [creatingNote, setCreatingNote] = useState(false);

  // Local edits tracking (noteId -> edited content)
  const [editedContent, setEditedContent] = useState<Record<number, string>>({});

  // Warn user before leaving with unsaved edits
  const hasUnsavedEdits = useMemo(() => Object.keys(editedContent).length > 0, [editedContent]);
  useEffect(() => {
    if (!hasUnsavedEdits) return;
    const handler = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = ""; };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [hasUnsavedEdits]);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [companyData, periodData] = await Promise.all([
        getCompany(cId),
        getPeriod(cId, pId),
      ]);
      setCompany(companyData);
      setPeriod(periodData);

      try {
        const notesData = await getNotes(cId, pId);
        setNotes(notesData);
      } catch {
        setNotes([]);
      }
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to load data");
    } finally {
      setLoading(false);
    }
  }, [cId, pId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  async function handleGenerateNotes() {
    setGenerating(true);
    try {
      const generatedNotes = await generateNotes(cId, pId);
      setNotes(generatedNotes);
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
      setNotes((prev) => prev.filter((n) => n.id !== noteId));
      setEditedContent((prev) => {
        const next = { ...prev };
        delete next[noteId];
        return next;
      });
      toast.success("Note deleted");
    } catch (err) {
      toast.error(err instanceof Error ? err.message : "Failed to delete note");
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

  if (loading) {
    return <PeriodWorkspaceSkeleton />;
  }

  if (!company) {
    return (
      <div className="max-w-2xl mx-auto">
        <Card className="border border-red-200 dark:border-red-800">
          <Card.Content className="text-center py-8">
            <AlertTriangle className="w-10 h-10 text-red-500 mx-auto mb-3" />
            <p className="text-red-700 dark:text-red-400 font-medium">Failed to load company data</p>
            <Button variant="outline" className="mt-4" onPress={loadData}>
              <RefreshCw className="w-4 h-4 mr-1" />
              Retry
            </Button>
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

      {/* Header */}
      <div className="mb-6">
        <Link
          href={`/companies/${companyId}/periods/${periodId}`}
          className="inline-flex items-center gap-1.5 text-sm text-gray-500 hover:text-emerald-600 dark:text-gray-400 dark:hover:text-emerald-400 mb-3"
        >
          <ArrowLeft className="w-4 h-4" />
          Back to Period Workspace
        </Link>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          Notes to the Financial Statements
        </h1>
        {company && period && (
          <p className="text-sm text-gray-500 dark:text-gray-400 mt-1">
            {company.legalName} &mdash;{" "}
            {new Date(period.periodStart).toLocaleDateString("en-IE")} to{" "}
            {new Date(period.periodEnd).toLocaleDateString("en-IE")}
          </p>
        )}
      </div>

      {/* Actions Bar */}
      <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 mb-6">
        <Card.Content className="p-4">
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <Button
                variant="primary"
                onPress={handleGenerateNotes}
                isDisabled={generating}
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
              </Button>
              <Button
                variant="outline"
                size="sm"
                onPress={() => setShowNewForm(true)}
              >
                <Plus className="w-4 h-4 mr-1" />
                Add Custom Note
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onPress={loadData}
                isDisabled={loading}
              >
                <RefreshCw
                  className={`w-4 h-4 mr-1 ${loading ? "animate-spin" : ""}`}
                />
                Refresh
              </Button>
            </div>
            <div className="flex items-center gap-3 text-sm text-gray-500 dark:text-gray-400">
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
      {showNewForm && (
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
      {sortedNotes.length === 0 ? (
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700">
          <Card.Content className="text-center py-12">
            <FileText className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
            <p className="text-sm text-gray-500 dark:text-gray-400">
              No notes have been created yet.
            </p>
            <p className="text-xs text-gray-400 dark:text-gray-500 mt-1">
              Click &ldquo;Generate Auto-Notes&rdquo; to create standard
              disclosures, or add a custom note.
            </p>
          </Card.Content>
        </Card>
      ) : (
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
                className={`shadow-sm border animate-fade-in ${
                  note.isIncluded
                    ? "border-gray-200 dark:border-neutral-700"
                    : "border-gray-200 dark:border-neutral-700 opacity-60"
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
                        </div>
                      </div>
                    </div>
                    <div className="flex items-center gap-2 shrink-0">
                      {/* Inclusion toggle */}
                      <label className="flex items-center gap-2 cursor-pointer">
                        <input
                          type="checkbox"
                          checked={note.isIncluded}
                          onChange={() => handleToggleIncluded(note)}
                          disabled={isSaving}
                          title={note.isIncluded ? "Exclude this note from the financial statements" : "Include this note in the financial statements"}
                          className="rounded border-gray-300 dark:border-neutral-600 text-emerald-600 focus:ring-emerald-500 dark:bg-neutral-800"
                        />
                        <span className="text-xs text-gray-500 dark:text-gray-400">
                          Include
                        </span>
                      </label>
                      {/* Delete */}
                      {!note.isRequired && (
                        <Button
                          variant="ghost"
                          size="sm"
                          onPress={() => handleDeleteNote(noteId)}
                          isDisabled={isDeleting}
                        >
                          {isDeleting ? (
                            <Spinner size="sm" />
                          ) : (
                            <Trash2 className="w-4 h-4 text-red-400 hover:text-red-600" />
                          )}
                        </Button>
                      )}
                    </div>
                  </div>

                  {/* Content editor */}
                  <textarea
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

                  {/* Save button - only visible when edits exist */}
                  {hasLocalEdits && (
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
                      <span className="text-xs text-amber-600 dark:text-amber-400">
                        Unsaved changes
                      </span>
                    </div>
                  )}
                </Card.Content>
              </Card>
            );
          })}
        </div>
      )}
    </div>
  );
}
