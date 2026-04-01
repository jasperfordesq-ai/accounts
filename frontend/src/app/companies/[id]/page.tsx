"use client";

import { useEffect, useState, use } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  Card, CardContent, CardHeader, CardTitle, CardDescription,
  Button, Chip, Spinner, TextField, Input, Label, Checkbox
} from "@heroui/react";
import {
  Building2, Users, Calendar, Plus, Trash2, ArrowRight, ChevronLeft
} from "lucide-react";
import { getCompany, deleteCompany, createPeriod, type Company } from "@/lib/api";

export default function CompanyDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const router = useRouter();
  const [company, setCompany] = useState<Company | null>(null);
  const [loading, setLoading] = useState(true);
  const [showNewPeriod, setShowNewPeriod] = useState(false);
  const [periodStart, setPeriodStart] = useState("");
  const [periodEnd, setPeriodEnd] = useState("");
  const [isFirstYear, setIsFirstYear] = useState(false);

  const load = () => {
    getCompany(Number(id))
      .then(setCompany)
      .catch(console.error)
      .finally(() => setLoading(false));
  };

  useEffect(() => { load(); }, [id]);

  const handleDelete = async () => {
    if (!confirm("Are you sure you want to delete this company and all its data?")) return;
    await deleteCompany(Number(id));
    router.push("/");
  };

  const handleCreatePeriod = async () => {
    if (!periodStart || !periodEnd) return;
    await createPeriod(Number(id), { periodStart, periodEnd, status: "Draft", isFirstYear });
    setShowNewPeriod(false);
    setPeriodStart("");
    setPeriodEnd("");
    load();
  };

  const statusColor = (status: string) => {
    switch (status) {
      case "Draft": return "default" as const;
      case "Review": return "accent" as const;
      case "Finalised": return "success" as const;
      case "Filed": return "success" as const;
      default: return "default" as const;
    }
  };

  if (loading) return <div className="flex justify-center py-20"><Spinner size="lg" /></div>;
  if (!company) return <div className="text-center py-12 text-red-500">Company not found</div>;

  return (
    <div>
      <Link href="/" className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4">
        <ChevronLeft className="w-4 h-4" /> Back to Dashboard
      </Link>

      <div className="flex items-start justify-between mb-8">
        <div className="flex items-center gap-3">
          <div className="bg-emerald-50 p-3 rounded-xl">
            <Building2 className="w-7 h-7 text-emerald-600" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">{company.legalName}</h1>
            {company.tradingName && <p className="text-gray-500">t/a {company.tradingName}</p>}
          </div>
        </div>
        <Button variant="danger" size="sm" onPress={handleDelete}>
          <Trash2 className="w-3.5 h-3.5" />
          Delete
        </Button>
      </div>

      {/* Info cards */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        <Card>
          <CardHeader>
            <CardTitle className="text-xs font-semibold text-gray-400 uppercase tracking-wide">Registration</CardTitle>
          </CardHeader>
          <CardContent className="space-y-2 text-sm">
            <div><span className="text-gray-500">CRO:</span> <span className="font-medium">{company.croNumber || "\u2014"}</span></div>
            <div><span className="text-gray-500">Tax Ref:</span> <span className="font-medium">{company.taxReference || "\u2014"}</span></div>
            <div><span className="text-gray-500">Type:</span> <span className="font-medium">{company.companyType}</span></div>
            <div><span className="text-gray-500">Incorporated:</span> <span className="font-medium">{company.incorporationDate}</span></div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-xs font-semibold text-gray-400 uppercase tracking-wide">Address</CardTitle>
          </CardHeader>
          <CardContent className="text-sm space-y-1">
            {company.registeredOfficeAddress1 && <div>{company.registeredOfficeAddress1}</div>}
            {company.registeredOfficeAddress2 && <div>{company.registeredOfficeAddress2}</div>}
            {company.registeredOfficeCity && <div>{company.registeredOfficeCity}</div>}
            {company.registeredOfficeCounty && <div>Co. {company.registeredOfficeCounty}</div>}
            {company.registeredOfficeEircode && <div>{company.registeredOfficeEircode}</div>}
            {!company.registeredOfficeAddress1 && <div className="text-gray-400">No address recorded</div>}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="text-xs font-semibold text-gray-400 uppercase tracking-wide flex items-center gap-2">
              <Users className="w-3.5 h-3.5" /> Officers
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            {company.officers && company.officers.length > 0 ? company.officers.map((o) => (
              <div key={o.id} className="text-sm">
                <span className="font-medium">{o.name}</span>
                <span className="text-gray-400 ml-2">({o.role})</span>
              </div>
            )) : (
              <div className="text-sm text-gray-400">No officers recorded</div>
            )}
          </CardContent>
        </Card>
      </div>

      {/* Accounting Periods */}
      <Card>
        <CardHeader className="flex flex-row items-center justify-between">
          <CardTitle className="flex items-center gap-2">
            <Calendar className="w-4 h-4 text-gray-400" />
            Accounting Periods
          </CardTitle>
          <Button variant="primary" size="sm" onPress={() => setShowNewPeriod(true)}>
            <Plus className="w-3.5 h-3.5" />
            New Period
          </Button>
        </CardHeader>

        {showNewPeriod && (
          <div className="px-6 py-4 bg-emerald-50 border-b border-emerald-100 flex items-end gap-4">
            <TextField className="w-44" value={periodStart} onChange={setPeriodStart}>
              <Label>Period Start</Label>
              <Input type="date" />
            </TextField>
            <TextField className="w-44" value={periodEnd} onChange={setPeriodEnd}>
              <Label>Period End</Label>
              <Input type="date" />
            </TextField>
            <Checkbox isSelected={isFirstYear} onChange={setIsFirstYear}>First year</Checkbox>
            <Button variant="primary" size="sm" onPress={handleCreatePeriod}>Create</Button>
            <Button variant="ghost" size="sm" onPress={() => setShowNewPeriod(false)}>Cancel</Button>
          </div>
        )}

        <CardContent>
          {company.periods && company.periods.length > 0 ? (
            <div className="divide-y divide-gray-100">
              {company.periods.map((period) => (
                <Link
                  key={period.id}
                  href={`/companies/${company.id}/periods/${period.id}`}
                  className="flex items-center justify-between py-4 hover:bg-gray-50 px-2 rounded-lg group"
                >
                  <div className="flex items-center gap-4">
                    <span className="text-sm font-medium">{period.periodStart} — {period.periodEnd}</span>
                    {period.isFirstYear && <Chip size="sm" color="warning" variant="soft">First Year</Chip>}
                    <Chip size="sm" color={statusColor(period.status)} variant="soft">{period.status}</Chip>
                    {period.sizeClassification && (
                      <span className="text-xs text-gray-400">Size: {period.sizeClassification.calculatedClass}</span>
                    )}
                  </div>
                  <ArrowRight className="w-4 h-4 text-gray-300 group-hover:text-emerald-500" />
                </Link>
              ))}
            </div>
          ) : (
            <div className="py-12 text-center text-gray-400 text-sm">
              No accounting periods yet. Create one to start preparing accounts.
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
