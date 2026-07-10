"use client";

import { Component, type ReactNode } from "react";
import { Button } from "@heroui/react";
import { AlertTriangle, RefreshCw } from "lucide-react";
import { reportClientMonitoringEvent } from "@/lib/clientMonitoring";

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
}

interface State {
  hasError: boolean;
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false };
  }

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch() {
    void reportClientMonitoringEvent("render-exception");
    console.error("A sanitized frontend render exception was reported.");
  }

  handleReset = () => {
    this.setState({ hasError: false });
  };

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) return this.props.fallback;

      return (
        <div className="flex flex-col items-center justify-center py-20 px-4 text-center">
          <div className="bg-red-50 dark:bg-red-900/20 p-4 rounded-full mb-4">
            <AlertTriangle className="w-10 h-10 text-red-500" />
          </div>
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-1">
            Something went wrong
          </h2>
          <p className="text-sm text-gray-500 dark:text-gray-400 max-w-md mb-4">
            An unexpected error occurred. Please try again.
          </p>
          <Button variant="outline" size="sm" onPress={this.handleReset}>
            <RefreshCw className="w-4 h-4 mr-1.5" />
            Try Again
          </Button>
        </div>
      );
    }

    return this.props.children;
  }
}
