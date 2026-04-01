"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CardDescription,
  CardFooter,
  Chip,
  Spinner,
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
} from "lucide-react";
import { getCompanies, type Company } from "@/lib/api";

export default function Dashboard() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    getCompanies()
      .then(setCompanies)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return (
      <div className="flex justify-center py-20">
        <Spinner size="lg" />
      </div>
    );
  }

  // Compute quick stats from companies
  const totalCompanies = companies.length;
  const totalPeriods = companies.reduce(
    (sum, c) => sum + (c.periodCount || 0),
    0
  );
  const tradingCompanies = companies.filter((c) => c.isTrading).length;
  const dormantCompanies = companies.filter((c) => c.isDormant).length;

  return (
    <div>
      <div className="flex items-center justify-between mb-8">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
          <p className="text-gray-500 mt-1">
            Irish Accounts Platform overview
          </p>
        </div>
        <Link href="/companies/new">
          <Button variant="primary" size="sm">
            <Plus className="w-4 h-4" />
            Add Company
          </Button>
        </Link>
      </div>

      {/* Quick Stats Bar */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 mb-8">
        <Card className="shadow-sm border border-gray-200">
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-emerald-50 p-2 rounded-lg">
                <Building2 className="w-5 h-5 text-emerald-600" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900">
                  {totalCompanies}
                </p>
                <p className="text-xs text-gray-500">Total Companies</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card className="shadow-sm border border-gray-200">
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-blue-50 p-2 rounded-lg">
                <Calendar className="w-5 h-5 text-blue-600" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900">
                  {totalPeriods}
                </p>
                <p className="text-xs text-gray-500">
                  Total Periods
                </p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card className="shadow-sm border border-gray-200">
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-amber-50 p-2 rounded-lg">
                <Clock className="w-5 h-5 text-amber-600" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900">
                  {tradingCompanies}
                </p>
                <p className="text-xs text-gray-500">Trading</p>
              </div>
            </div>
          </CardContent>
        </Card>

        <Card className="shadow-sm border border-gray-200">
          <CardContent className="p-4">
            <div className="flex items-center gap-3">
              <div className="bg-gray-100 p-2 rounded-lg">
                <AlertCircle className="w-5 h-5 text-gray-500" />
              </div>
              <div>
                <p className="text-2xl font-bold text-gray-900">
                  {dormantCompanies}
                </p>
                <p className="text-xs text-gray-500">Dormant</p>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>

      {/* Companies Section */}
      <div className="mb-4">
        <h2 className="text-lg font-semibold text-gray-900">Companies</h2>
        <p className="text-sm text-gray-500">
          Manage your Irish company accounts
        </p>
      </div>

      {companies.length === 0 ? (
        <Card className="text-center py-16">
          <CardContent>
            <Building2 className="w-12 h-12 text-gray-300 mx-auto mb-4" />
            <CardTitle className="text-lg mb-2">No companies yet</CardTitle>
            <CardDescription className="mb-6">
              Add your first company to get started with accounts preparation.
            </CardDescription>
            <Link href="/companies/new">
              <Button variant="primary" size="sm">
                <Plus className="w-4 h-4" />
                Add Company
              </Button>
            </Link>
          </CardContent>
        </Card>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {companies.map((company) => (
            <Link key={company.id} href={`/companies/${company.id}`}>
              <Card className="hover:border-emerald-300 hover:shadow-md transition-all cursor-pointer h-full">
                <CardHeader className="flex flex-row items-start justify-between">
                  <div className="bg-emerald-50 p-2 rounded-lg">
                    <Building2 className="w-5 h-5 text-emerald-600" />
                  </div>
                  <div className="flex items-center gap-2">
                    <Chip
                      size="sm"
                      variant="soft"
                      color={
                        company.isTrading
                          ? "success"
                          : company.isDormant
                            ? "default"
                            : "warning"
                      }
                    >
                      {company.isTrading
                        ? "Trading"
                        : company.isDormant
                          ? "Dormant"
                          : "Inactive"}
                    </Chip>
                    <ArrowRight className="w-4 h-4 text-gray-300" />
                  </div>
                </CardHeader>
                <CardContent>
                  <CardTitle className="text-base">
                    {company.legalName}
                  </CardTitle>
                  {company.tradingName && (
                    <CardDescription>
                      t/a {company.tradingName}
                    </CardDescription>
                  )}

                  {/* Company details */}
                  <div className="mt-3 space-y-1.5">
                    {company.croNumber && (
                      <p className="text-xs text-gray-500">
                        CRO: {company.croNumber}
                      </p>
                    )}
                    {company.taxReference && (
                      <p className="text-xs text-gray-500">
                        Tax Ref: {company.taxReference}
                      </p>
                    )}
                    <p className="text-xs text-gray-500">
                      Type: {company.companyType}
                    </p>
                  </div>
                </CardContent>
                <CardFooter className="flex items-center justify-between border-t border-gray-100 pt-3">
                  <div className="flex items-center gap-1.5 text-xs text-gray-500">
                    <FileText className="w-3.5 h-3.5" />
                    <span>
                      {company.periodCount || 0} period
                      {(company.periodCount || 0) !== 1 ? "s" : ""}
                    </span>
                  </div>
                  <div className="flex items-center gap-2">
                    {company.isVatRegistered && (
                      <Chip size="sm" variant="soft" color="accent">
                        VAT
                      </Chip>
                    )}
                    {company.isEmployer && (
                      <Chip size="sm" variant="soft" color="accent">
                        Employer
                      </Chip>
                    )}
                    {(company.periodCount || 0) > 0 ? (
                      <CheckCircle2 className="w-4 h-4 text-emerald-500" />
                    ) : (
                      <AlertCircle className="w-4 h-4 text-amber-400" />
                    )}
                  </div>
                </CardFooter>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
