/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PaginatedResultOfCatalogSegmentDto } from '../models/PaginatedResultOfCatalogSegmentDto';
import type { PaginatedResultOfCategoryDto } from '../models/PaginatedResultOfCategoryDto';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class SegmentsService {
    /**
     * @returns PaginatedResultOfCatalogSegmentDto OK
     * @throws ApiError
     */
    public static getAllSegments({
        page,
        pageSize,
    }: {
        page: number | string,
        pageSize: number | string,
    }): CancelablePromise<PaginatedResultOfCatalogSegmentDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/segments',
            query: {
                'page': page,
                'pageSize': pageSize,
            },
        });
    }
    /**
     * @returns PaginatedResultOfCategoryDto OK
     * @throws ApiError
     */
    public static getCategoriesBySegment({
        id,
        page,
        pageSize,
    }: {
        id: number,
        page: number | string,
        pageSize: number | string,
    }): CancelablePromise<PaginatedResultOfCategoryDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/segments/{id}/categories',
            path: {
                'id': id,
            },
            query: {
                'page': page,
                'pageSize': pageSize,
            },
        });
    }
}
