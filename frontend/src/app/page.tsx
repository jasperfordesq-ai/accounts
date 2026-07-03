"use client";

import { useEffect, useState, useMemo } from "react";
import Link from "next/link";
import {
  Card,
  Chip,
  Button,
} from "@heroui/react";
import {
  Building2,
  Calendar,
  ArrowRight,
  Plus,
  FileText,
  AlertCircle,
  CheckCircle2,
  Clock,
  Search,
  X,
} from "lucide-react";
import { getCompanies, getUpcomingDeadline, type Company, type FilingDeadline } from "@/lib/api";
import { DashboardSkeleton } from "@/components/Skeleton";
import { formatCompanyType, formatDateIE } from "@/lib/format";
import { useAuth } from "@/components/AuthProvider";

export default function Dashboard() {
  const { isOwner } = useAuth();
  const [companies, setCompanies] = useState<Company[]>([]);
  const [deadlines, setDeadlines] = useState<Record<number, FilingDeadline | null>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");

  useEffect(() => {
    getCompanies()
      .then(async (data) => {
        setCompanies(data);
        // Fetch upcoming deadlines for each company
        const deadlineMap: Record<number, FilingDeadline | null> = {};
        await Promise.all(
          data.map(async (c) => {
            try {
              const d = await getUpcomingDeadline(c.id);
              deadlineMap[c.id] = d && "dueDate" in d ? (d as FilingDeadline) : null;
            } catch {
              deadlineMap[c.id] = null;
            }
          })
        );
        setDeadlines(deadlineMap);
      })
      .catch((err) => setError(err instanceof Error ? err.message : "Failed to load companies"))
      .finally(() => setLoading(false));
  }, []);

  const filtered = useMemo(() => {
    if (!search.trim()) return companies;
    const q = search.toLowerCase();
    return companies.filter(
      (c) =>
        c.legalName?.toLowerCase().includes(q) ||
        c.tradingName?.toLowerCase().includes(q) ||
        c.croNumber?.toLowerCase().includes(q) ||
        c.companyType?.toLowerCase().includes(q) ||
        formatCompanyType(c.companyType).toLowerCase().includes(q)
    );
  }, [companies, search]);

  const deadlineThresholds = useMemo(() => {
    const now = new Date();
    return {
      now,
      warning: new Date(now.getTime() + 30 * 86400000),
    };
  }, []);

  if (loading) return <DashboardSkeleton />;

  // Compute quick stats from companies
  const totalCompanies = companies.length;
  const totalPeriods = companies.reduce(
    (sum, c) => sum + (c.periodCount || 0),
    0
  );
  const tradingCompanies = companies.filter((c) => c.isTrading).length;
  const dormantCompanies = companies.filter((c) => c.isDormant).length;

  return (
    <div className="animate-fade-in">
      <div className="flex items-center justify-between mb-8">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-gray-100">
            Dashboard
          </h1>
          <p className="text-gray-500 dark:text-gray-400 mt-1">
            Irish Accounts Platform overview
          </p>
        </div>
        {isOwner && (
          <Link href="/companies/new">
            <Button variant="primary" size="sm">
              <Plus className="w-4 h-4" />
              Add Company
            </Button>
          </Link>
        )}
      </div>

      {/* Error state */}
      {error && (
        <div className="rounded-lg bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 px-4 py-3 text-sm text-red-700 dark:text-red-400 mb-6 flex items-center gap-2">
          <AlertCircle className="w-4 h-4 shrink-0" />
          {error}
        </div>
      )}

      {/* Quick Stats Bar */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-emerald-50 dark:bg-emerald-900/30 p-2 rounded-lg">
                <Building2 className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                  {totalCompanies}
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Companies
                </p>
              </div>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-blue-50 dark:bg-blue-900/30 p-2 rounded-lg">
                <Calendar className="w-5 h-5 text-blue-600 dark:text-blue-400" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                  {totalPeriods}
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Periods
                </p>
              </div>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-purple-50 dark:bg-purple-900/30 p-2 rounded-lg">
                <CheckCircle2 className="w-5 h-5 text-purple-600 dark:text-purple-400" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                  {tradingCompanies}
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Trading
                </p>
              </div>
            </div>
          </Card.Content>
        </Card>

        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-amber-50 dark:bg-amber-900/30 p-2 rounded-lg">
                <Clock className="w-5 h-5 text-amber-600 dark:text-amber-400" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                  {dormantCompanies}
                </p>
                <p className="text-xs text-gray-500 dark:text-gray-400">
                  Dormant
                </p>
              </div>
            </div>
          </Card.Content>
        </Card>
      </div>

      {/* Companies section with search */}
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
          Companies
        </h2>
        <div className="relative w-64">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            placeholder="Search companies..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-9 pr-8 py-2 text-sm rounded-lg border border-gray-300 dark:border-neutral-600 bg-white dark:bg-neutral-800 text-gray-900 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 outline-none focus:ring-2 focus:ring-emerald-500 focus:border-emerald-500 transition-colors"
            aria-label="Search companies"
          />
          {search && (
            <button
              onClick={() => setSearch("")}
              className="absolute right-2.5 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
              aria-label="Clear search"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>
      </div>

      {filtered.length === 0 && companies.length > 0 ? (
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="text-center py-12">
            <Search className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
            <p className="text-sm text-gray-500 dark:text-gray-400">
              No companies match &ldquo;{search}&rdquo;
            </p>
            <Button
              variant="ghost"
              size="sm"
              className="mt-3"
              onPress={() => setSearch("")}
            >
              Clear search
            </Button>
          </Card.Content>
        </Card>
      ) : filtered.length === 0 ? (
        <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900">
          <Card.Content className="text-center py-12">
            <Building2 className="w-10 h-10 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
            <p className="text-gray-500 dark:text-gray-400 font-medium">
              No companies available
            </p>
            {isOwner && (
              <Link href="/companies/new" className="inline-block mt-4">
                <Button variant="primary" size="sm">
                  <Plus className="w-4 h-4" />
                  Add Company
                </Button>
              </Link>
            )}
          </Card.Content>
        </Card>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {filtered.map((company) => (
            <Link key={company.id} href={`/companies/${company.id}`}>
              <Card className="shadow-sm border border-gray-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 card-hover cursor-pointer h-full">
                <Card.Content className="p-5">
                  <div className="flex items-start gap-3 mb-3">
                    <div className="bg-emerald-50 dark:bg-emerald-900/30 p-2.5 rounded-lg shrink-0">
                      <Building2 className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <h3 className="font-semibold text-gray-900 dark:text-gray-100 truncate">
                        {company.legalName}
                      </h3>
                      {company.tradingName && (
                        <p className="text-sm text-gray-500 dark:text-gray-400 truncate">
                          t/a {company.tradingName}
                        </p>
                      )}
                    </div>
                    <ArrowRight className="w-4 h-4 text-gray-300 dark:text-gray-600 shrink-0 mt-1" />
                  </div>

                  <div className="flex flex-wrap items-center gap-2">
                    <Chip size="sm" variant="soft" color="default">
                      {formatCompanyType(company.companyType)}
                    </Chip>
                    {company.isTrading && (
                      <Chip size="sm" variant="soft" color="success">
                        <CheckCircle2 className="w-3 h-3" />
                        Trading
                      </Chip>
                    )}
                    {company.isDormant && (
                      <Chip size="sm" variant="soft" color="warning">
                        <Clock className="w-3 h-3" />
                        Dormant
                      </Chip>
                    )}
                    {(company.periodCount ?? 0) > 0 && (
                      <Chip size="sm" variant="soft" color="accent">
                        <FileText className="w-3 h-3" />
                        {company.periodCount} period{company.periodCount !== 1 ? "s" : ""}
                      </Chip>
                    )}
                    {deadlines[company.id] && (
                      <Chip
                        size="sm"
                        variant="soft"
                        color={
                          new Date(deadlines[company.id]!.dueDate) < deadlineThresholds.now
                            ? "danger"
                            : new Date(deadlines[company.id]!.dueDate) < deadlineThresholds.warning
                              ? "warning"
                              : "default"
                        }
                      >
                        <Clock className="w-3 h-3" />
                        {deadlines[company.id]!.deadlineType} due{" "}
                        {formatDateIE(deadlines[company.id]!.dueDate)}
                      </Chip>
                    )}
                  </div>

                  {company.croNumber && (
                    <p className="text-xs text-gray-400 dark:text-gray-500 mt-3">
                      CRO: {company.croNumber}
                    </p>
                  )}
                </Card.Content>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
