import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../../core/api/api.config';
import { ReportSummary } from '../models/report.models';
@Injectable({ providedIn: 'root' })
export class ReportService { private readonly http=inject(HttpClient); getMine(fromDate?:string,toDate?:string):Observable<ReportSummary>{let params=new HttpParams();if(fromDate)params=params.set('fromDate',fromDate);if(toDate)params=params.set('toDate',toDate);return this.http.get<ReportSummary>(`${API_BASE_URL}/reports/me`,{params});} }
