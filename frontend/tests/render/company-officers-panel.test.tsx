import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CompanyOfficersPanel } from "@/components/company/CompanyOfficersPanel";
import type { Officer } from "@/lib/api";

describe("CompanyOfficersPanel", () => {
  it("renders officers and wires primary actions", () => {
    const onShowAddOfficer = vi.fn();
    const onStartEditOfficer = vi.fn();
    const onDeleteOfficer = vi.fn();
    const officers = sampleOfficers();

    const { container } = render(
      <CompanyOfficersPanel
        officers={officers}
        showAddOfficer={false}
        newOfficerName=""
        newOfficerRole="Director"
        editingOfficerId={null}
        editOfficerName=""
        editOfficerRole="Director"
        savingOfficer={false}
        onShowAddOfficer={onShowAddOfficer}
        onNewOfficerNameChange={vi.fn()}
        onNewOfficerRoleChange={vi.fn()}
        onCancelAddOfficer={vi.fn()}
        onAddOfficer={vi.fn()}
        onStartEditOfficer={onStartEditOfficer}
        onEditOfficerNameChange={vi.fn()}
        onEditOfficerRoleChange={vi.fn()}
        onSaveOfficer={vi.fn()}
        onCancelEditOfficer={vi.fn()}
        onDeleteOfficer={onDeleteOfficer}
      />,
    );

    expect(screen.getByText("Officers & Signatories")).toBeInTheDocument();
    expect(screen.getByText("Directors, secretary and statutory signatory records.")).toBeInTheDocument();
    expect(screen.getByText("2 officers")).toBeInTheDocument();
    expect(screen.getByText("Niamh Director")).toBeInTheDocument();
    expect(screen.getByText("Sean Secretary")).toBeInTheDocument();
    expect(screen.getAllByText("Active")).toHaveLength(2);
    expect(screen.getByRole("button", { name: "Sort by Officer" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sort by Role" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sort by Actions" })).not.toBeInTheDocument();
    expect(screen.getByRole("columnheader", { name: "Actions" })).not.toHaveAttribute("aria-sort");
    expect(container.querySelector(".workbench-data-grid")).toHaveAttribute("data-responsive", "card");
    expect(container.querySelector('td[data-label="Actions"]')).toContainElement(
      screen.getByRole("button", { name: "Edit Niamh Director" }),
    );

    fireEvent.click(screen.getByRole("button", { name: "Add officer" }));
    fireEvent.click(screen.getByRole("button", { name: "Edit Niamh Director" }));
    fireEvent.click(screen.getByRole("button", { name: "Remove Sean Secretary" }));
    expect(onDeleteOfficer).not.toHaveBeenCalled();
    expect(screen.getByRole("alertdialog", { name: "Remove officer Sean Secretary?" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Remove record" }));

    expect(onShowAddOfficer).toHaveBeenCalledTimes(1);
    expect(onStartEditOfficer).toHaveBeenCalledWith(officers[0]);
    expect(onDeleteOfficer).toHaveBeenCalledWith(2);
  });

  it("hides write actions when the user cannot edit working papers", () => {
    render(
      <CompanyOfficersPanel
        officers={sampleOfficers()}
        showAddOfficer={false}
        newOfficerName=""
        newOfficerRole="Director"
        editingOfficerId={null}
        editOfficerName=""
        editOfficerRole="Director"
        savingOfficer={false}
        canWrite={false}
        onShowAddOfficer={vi.fn()}
        onNewOfficerNameChange={vi.fn()}
        onNewOfficerRoleChange={vi.fn()}
        onCancelAddOfficer={vi.fn()}
        onAddOfficer={vi.fn()}
        onStartEditOfficer={vi.fn()}
        onEditOfficerNameChange={vi.fn()}
        onEditOfficerRoleChange={vi.fn()}
        onSaveOfficer={vi.fn()}
        onCancelEditOfficer={vi.fn()}
        onDeleteOfficer={vi.fn()}
      />,
    );

    expect(screen.queryByRole("button", { name: "Add officer" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Edit Niamh Director" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remove Sean Secretary" })).not.toBeInTheDocument();
    expect(screen.getAllByText("Read only")).toHaveLength(2);
  });
});

function sampleOfficers(): Officer[] {
  return [
    { id: 1, name: "Niamh Director", role: "Director" },
    { id: 2, name: "Sean Secretary", role: "Secretary" },
  ];
}
