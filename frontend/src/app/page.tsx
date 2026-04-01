"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Card, CardContent, CardHeader, CardTitle, CardDescription, CardFooter, Chip, Spinner, Button } from "@heroui/react";
import { Building2, Calendar, ArrowRight, Plus } from "lucide-react";
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

  return (
    <div>
      <div className="flex items-center justify-between mb-8">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Companies</h1>
          <p className="text-gray-500 mt-1">Manage your Irish company accounts</p>
        </div>
        <Link href="/companies/new">
          <Button variant="primary" size="sm">
            <Plus className="w-4 h-4" />
            Add Company
          </Button>
        </Link>
      </div>

      {companies.length === 0 ? (
        <Card className="text-center py-16">
          <CardContent>
            <Building2 className="w-12 h-12 text-gray-300 mx-auto mb-4" />
            <CardTitle className="text-lg mb-2">No companies yet</CardTitle>
            <CardDescription className="mb-6">Add your first company to get started with accounts preparation.</CardDescription>
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
                  <ArrowRight className="w-4 h-4 text-gray-300" />
                </CardHeader>
                <CardContent>
                  <CardTitle className="text-base">{company.legalName}</CardTitle>
                  {company.tradingName && (
                    <CardDescription>t/a {company.tradingName}</CardDescription>
                  )}
                </CardContent>
                <CardFooter className="flex items-center justify-between">
                  <div className="flex items-center gap-3 text-xs text-gray-400">
                    {company.croNumber && <span>CRO: {company.croNumber}</span>}
                    <span className="flex items-center gap-1">
                      <Calendar className="w-3 h-3" />
                      {company.periodCount || 0} period{(company.periodCount || 0) !== 1 ? "s" : ""}
                    </span>
                  </div>
                  <Chip
                    size="sm"
                    variant="soft"
                    color={company.isTrading ? "success" : company.isDormant ? "default" : "warning"}
                  >
                    {company.isTrading ? "Trading" : company.isDormant ? "Dormant" : "Inactive"}
                  </Chip>
                </CardFooter>
              </Card>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
